using System;
using System.Collections;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using ServiceActivityLogger;

/* Command line parameter example
 * -f "\\WKS004\Install\test\SBS\Mec\Service2" "WORKING" 900 1800
 * -r "\\WKS004\Install\test\SBS\Mec\" "WORKING" 900 1800
 * -c "\\WKS004\Install\test\SBS\Mec\Service3\Mec.mecc" -o "CustomName.sal" "WORKING" 900 1800
 */
namespace sla
{
	class Program
	{
		static void Main(string[] args)
		{
			string configFile = "";
			string pathToLogFile = "";
			string logFileName = "ServiceActivityLog.sal";
            string sysLogFile = string.Empty;
            bool verbose = false;
            if (args.Length < 1)
			{
				Console.WriteLine("Usage:");
				Console.Write(" " + System.AppDomain.CurrentDomain.FriendlyName);
                Console.Write(" {-r Log_File_Root_Dir | -f Log_File_Path | -c Config_File} [-o Log_File_Name] [-v]");
                Console.WriteLine("...");
                Console.WriteLine("Note: -r, -c and -f are mutually exclusive");
				Console.WriteLine("      config params other than -c, -f, -r or -o are ignored");
				Console.WriteLine("      rest of parameters are directly written to log file");
				Console.WriteLine("      default value for optional parameter -o is " + logFileName);
                Console.WriteLine("      -v to write a debug log at destination root");
                return;
			}
			ArrayList logItems = new ArrayList();

			for (int i = 0; i < args.Length; ++i)
			{
				if (args[i].StartsWith("-"))
				{
					switch(args[i]) {
                        case "-r":
                            ++i;
                            pathToLogFile = Path.Combine(args[i], ParentProcess.DirectoryName);
                            sysLogFile = args[i];
                            break;
                        case "-f":
                            ++i;
							pathToLogFile = args[i];
                            sysLogFile = args[i];
                            break;
						case "-c":
                            ++i;
							configFile = args[i];
							break;
						case "-o":
                            ++i;
							logFileName = args[i];
							break;
                        case "-v":
                            verbose = true;
                            break;
						default:
                            ++i;
							Console.Write("Unsupported param " + args[i]);
							if(args.Length - 1 >= i)
								Console.WriteLine(" " + args[i]);
							break;
					}					
				}
				else
					logItems.Add(args[i]);
			}
            if (verbose)
                sysLogFile = Path.Combine(sysLogFile, ParentProcess.DirectoryName) + ".log";
            else
                sysLogFile = string.Empty;

            if (!sysLogFile.Equals(string.Empty))
                File.AppendAllText(sysLogFile, "Process " + ParentProcess.DirectoryName + Environment.NewLine);

            if (configFile.Length > 0 && pathToLogFile.Length > 0)
			{
                if (!sysLogFile.Equals(string.Empty))
                    File.AppendAllText(sysLogFile, "Process " + ParentProcess.DirectoryName + " Please provide one of -f or -r parameters" + Environment.NewLine);
				return;
			}

			if (pathToLogFile.Length == 0)
			{
				try
				{
					string[] lines = Regex.Split(File.ReadAllText(configFile), "\r\n");
					string remoteRoot = "";
					string remoteDir = "";
					foreach (string line in lines)
					{
						string[] nv = Regex.Split(line, "=");
						if (nv[0].Equals("RemoteRoot"))
							remoteRoot = nv[1];
						if (nv[0].Equals("RemoteDirectory"))
							remoteDir = nv[1];
					}
					if (remoteRoot.Equals(string.Empty))
						throw new Exception("missing mandatory config 'RemoteRoot'");

					pathToLogFile = Path.Combine(remoteRoot, remoteDir);
				}
				catch (Exception e)
				{
                    if (!sysLogFile.Equals(string.Empty))
                        File.AppendAllText(sysLogFile, "Process " + ParentProcess.DirectoryName + " Error reading config file " + configFile + ": " + e.Message + Environment.NewLine);
					return;
				}
			}

            string logFile = Path.Combine(pathToLogFile, logFileName);
            if (!sysLogFile.Equals(string.Empty))
                File.AppendAllText(sysLogFile, "Process " + ParentProcess.DirectoryName + " Log file " + logFile + Environment.NewLine);
            logItems.Insert(0, DateTime.Now.ToString("yyyy'-'MM'-'dd HH:mm:ss"));
			logItems.Insert(0, System.Environment.MachineName);

			string[] content = logItems.ToArray(Type.GetType("System.String")) as string[];

			try
			{
				File.WriteAllLines(logFile, content);
			}
			catch (Exception e)
			{
                if (!sysLogFile.Equals(string.Empty))
                    File.AppendAllText(sysLogFile, "Process " + ParentProcess.DirectoryName + " Error writing to log file: " + e.Message + Environment.NewLine);
			}
		}
	}
}