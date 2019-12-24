﻿using gsudo.Commands;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace gsudo.Helpers
{
    class ArgumentsHelper
    {
        internal static string[] AugmentCommand(string[] args)
        {
            // Is our current shell Powershell ? (Powershell.exe -calls-> gsudo)
            var parentProcess = Process.GetCurrentProcess().ParentProcess();
            var parentExeName = Path.GetFileName(parentProcess.MainModule.FileName).ToUpperInvariant();
            if (parentExeName == "POWERSHELL.EXE")
            {
                if (args.Length == 0)
                    return new string[]
                        { parentProcess.MainModule.FileName };
                else
                    return new string[] // Escape " => ""
                        { parentProcess.MainModule.FileName , $"\"{ String.Join(" ", args.Select(arg => arg.Replace("\"","\"\""))) }\""};
            }
            else
            {
                // Is our current shell Powershell Core? (Pwsh.exe -calls-> dotnet -calls-> gsudo)
                var grandParentProcess = parentProcess.ParentProcess();
                if (grandParentProcess != null)
                {
                    var grandParentExeName = Path.GetFileName(grandParentProcess.MainModule.FileName).ToUpperInvariant();
                    if (grandParentExeName == "PWSH.EXE")
                    {
                        if (args.Length == 0)
                            return new string[]
                                { grandParentProcess.MainModule.FileName };
                        else
                            return new string[] // Escape " => `"
                                { grandParentProcess.MainModule.FileName , $"\"{ String.Join(" ", args.Select(arg => arg.Replace("\"","'\""))) }\""};
                    }
                }
            }

            // Not Powershell, or Powershell Core, assume CMD.
            if (args.Length == 0)
            {
                // If zero args specified, elevate CMD.

                // Default, our current shell is CMD.
                return new string[]
                    { Environment.GetEnvironmentVariable("COMSPEC"), "/k" };
            }
            else
            {
                // add "CMD /C" prefix to commands such as MD, CD, DIR..
                // We are sure this will not be an interactive experience, 
                // so we can safely use raw 

                return new string[]
                    { Environment.GetEnvironmentVariable("COMSPEC"), "/c" }
                    .Concat(args).ToArray();
            }
        }

        internal static int? ParseCommonSettings(ref string[] args)
        {
            Stack<string> stack = new Stack<string>(args.Reverse());

            while (stack.Any())
            {
                var arg = stack.Peek();
                if (arg.In("-v", "--version"))
                {
                    HelpCommand.ShowVersion();
                    return 0;
                }
                else if (arg.In("-h", "--help", "help", "/?"))
                {
                    HelpCommand.ShowHelp();
                    return 0;
                }
                else if (arg.In("-n", "--new"))
                {
                    GlobalSettings.NewWindow = true;
                    stack.Pop();
                }
                else if (arg.In("-w", "--wait"))
                {
                    GlobalSettings.Wait = true;
                    stack.Pop();
                }
                else if (arg.In("--debug"))
                {
                    GlobalSettings.Debug = true;
                    GlobalSettings.LogLevel.Value = LogLevel.All;
                    stack.Pop();
                }
                else if (arg.In("--loglevel"))
                {
                    stack.Pop();
                    arg = stack.Pop();
                    try
                    {
                        GlobalSettings.LogLevel.Value = (LogLevel)Enum.Parse(typeof(LogLevel), arg, true);
                    }
                    catch
                    {
                        Logger.Instance.Log($"\"{arg}\" is not a valid LogLevel. Valid values are: All, Debug, Info, Warning, Error, None", LogLevel.Error);
                        return Constants.GSUDO_ERROR_EXITCODE;
                    }
                }
                else if (arg.In("--raw"))
                {
                    GlobalSettings.ForceRawConsole.Value = true;
                    stack.Pop();
                }
                else if (arg.In("--vt"))
                {
                    GlobalSettings.ForceVTConsole.Value = true;
                    stack.Pop();
                }
                else if (arg.StartsWith("-", StringComparison.Ordinal))
                {
                    Logger.Instance.Log($"Invalid option: {arg}", LogLevel.Error);
                    return Constants.GSUDO_ERROR_EXITCODE;
                }
                else
                {
                    break;
                }
            }

            args = stack.ToArray();
            return null;
        }

        internal static ICommand ParseCommand(string[] args)
        {
            if (args.Length == 0) return new RunCommand() { CommandToRun = Array.Empty<string>() };

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase))
                return new RunCommand() { CommandToRun = args.Skip(1) };

            if (args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
                return new HelpCommand();

            if (args[0].Equals("gsudoservice", StringComparison.OrdinalIgnoreCase))
            {
                bool hasLoglevel = false;
                LogLevel logLevel = LogLevel.Info;
                if (args.Length>2)
                {
                    hasLoglevel = Enum.TryParse<LogLevel>(args[2], true, out logLevel);
                }

                return new ServiceCommand()
                {
                    allowedPid = int.Parse(args[1], CultureInfo.InvariantCulture),
                    LogLvl = hasLoglevel ? logLevel : (LogLevel?)null,
                };
            }

            if (args[0].Equals("gsudoctrlc", StringComparison.OrdinalIgnoreCase))
                return new CtrlCCommand() { pid = int.Parse(args[1], CultureInfo.InvariantCulture) };

            if (args[0].Equals("config", StringComparison.OrdinalIgnoreCase))
                return new ConfigCommand() { key = args.Skip(1).FirstOrDefault(), value = args.Skip(2) };

            return new RunCommand() { CommandToRun = args };
        }

        internal static string[] GetRealCommandLine()
        {
            System.IntPtr ptr = GetCommandLine();
            string commandLine = Marshal.PtrToStringAuto(ptr);
            if (commandLine[0] == '"')
                return commandLine.Substring(commandLine.IndexOf('"', 1) + 1).TrimStart(' ').Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            else if (commandLine.IndexOf(' ', 1) >= 0)
                return commandLine.Substring(commandLine.IndexOf(' ', 1) + 1).TrimStart(' ').Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            else
                return Array.Empty<string>();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern System.IntPtr GetCommandLine();

    }
}
