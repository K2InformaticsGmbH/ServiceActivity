using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ServiceActivityNagiosPlugin
{
    public class Message
    {
        public string msg;
        public double tdiff;

        public Message(string _msg, double _tdiff)
        {
            msg = _msg;
            tdiff = _tdiff;
        }
    };
    public class MessageComparer : IComparer
    {
        int IComparer.Compare(Object x, Object y)
        {
            return (int)(((Message)y).tdiff - ((Message)x).tdiff);
        }
    };

	class Program
	{
        const int NAGIOS_OK = 0;
		const int NAGIOS_WARNING = 1;
		const int NAGIOS_CRITICAL = 2;
		const int NAGIOS_UNKNOWN = 3;
		const string clusterSrvCmd = "hagrp -display NagiosLeader | find \"ONLINE\""; //002BC

        static int ret = NAGIOS_OK;
		static string machineName = "";
		static int verbosity = 0;
        static ArrayList warnMessages = new ArrayList();
        static ArrayList critMessages = new ArrayList();

        /*static int parseArg(string arg, ref int start, ref int end)
		{
			bool hasAt = arg.Trim().StartsWith("@");

			string[] ArgElms = Regex.Split(arg.TrimStart('@'), ":");
			int s = int.MinValue, e = int.MaxValue;
			if (ArgElms.Length > 1 && !ArgElms[1].Equals(string.Empty))
			{
				if (!ArgElms[0].Equals("~"))
					s = int.Parse(ArgElms[0]);
				if (ArgElms[1].Equals("~"))
					return -1;
				e = int.Parse(ArgElms[1]);
			}
			else if (ArgElms.Length > 1 && ArgElms[1].Equals(string.Empty))
			{
				if (!ArgElms[0].Equals("~"))
					s = int.Parse(ArgElms[0]);
			}
			else
			{
				s = 0;
				if (ArgElms[0].Equals("~"))
					return -1;
				e = int.Parse(ArgElms[0]);
			}

			if (hasAt) { start = e; end = s; }
			else { start = s; end = e; }

			return 0;
		}*/

        static int Main(string[] args)
        {
            string appName = System.AppDomain.CurrentDomain.FriendlyName;

            if (args.Length < 1)
            {
                Console.WriteLine("Usage: " + appName + " Path_To_Root_Folder ...");
                return NAGIOS_UNKNOWN;
            }
            string path = args[0];

            for (int i = 1; i < args.Length; ++i)
            {
                if (args[i].StartsWith("-"))
                {
                    switch (args[i])
                    {
                        case "-w":
                        case "--warning":
                            ++i;
                            //if (parseArg(args[i], ref warnMin, ref warnMax) < 0)
                            //	return NAGIOS_UNKNOWN;
                            break;
                        case "-c":
                        case "--critical":
                            ++i;
                            //if (parseArg(args[i], ref critMin, ref critMax) < 0)
                            //	return NAGIOS_UNKNOWN;
                            break;
                        case "--verbose":
                            ++i;
                            int v = int.Parse(args[i]);
                            if (v > verbosity)
                                verbosity = v;
                            break;
                        case "-V":
                        case "--version":
                            Console.WriteLine(appName + " version " + Assembly.GetExecutingAssembly().GetName().Version);
                            return NAGIOS_UNKNOWN;
                        case "-h":
                        case "--help":
                        case "-?":
                        default:
                            Console.WriteLine("Usage: " + appName + " Path_TO_Root_Folder ...");
                            return NAGIOS_UNKNOWN;
                    }
                }
                else
                {
                    Console.WriteLine("Usage: " + appName + " Path_TO_Root_Folder ...");
                    return NAGIOS_UNKNOWN;
                }
            }

#if DEBUG
            Console.WriteLine("DEBUG: Path " + path);
#endif

#if HAGRP_CHECK
            try
            {
#if DEBUG
                Console.WriteLine("DEBUG: Preparing hrgp command '" + clusterSrvCmd + "' for execution");
#endif
                // Prepare to execute 'hagrp' command
                ProcessStartInfo procStartInfo = new ProcessStartInfo("cmd", "/c " + clusterSrvCmd);
                procStartInfo.RedirectStandardOutput = true;
                procStartInfo.UseShellExecute = false;

#if DEBUG
                Console.WriteLine("DEBUG: Executing hrgp");
#endif

                // Execute 'hagrp' command
                Process proc = new Process();
                proc.StartInfo = procStartInfo;
                proc.Start();

#if DEBUG
                Console.WriteLine("DEBUG: Reading hrgp return...");
#endif

                // Get the output into a string
                string result = proc.StandardOutput.ReadToEnd();

#if DEBUG
                Console.WriteLine("DEBUG: hrgp returned '" + result + "'");
#endif

                // Result should be of the form - 
                //          'NagiosLeader State                 localclus:ZHHAPMOP-SBSA01 |ONLINE|'  //003BC
                // cut the Host name out
                int idx = 0;
                foreach (string word in Regex.Split(result, " "))
                {
                    if (word.Equals(string.Empty))
                        continue;
                    else
                        idx++;

                    if (idx == 3)
                    {
                        //003BC
                        string[] mParts = word.Trim().Split(':');
                        if(mParts.Length > 1)
                            machineName = mParts[1];
                        else
                            machineName = mParts[0];
                        break;
                    }
                };
            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR: Unable to execute " + clusterSrvCmd + ": " + e.Message);
                return NAGIOS_UNKNOWN;
            }
#else
            machineName = System.Environment.MachineName;
#endif
            try
            {
                // If we are not the ClusterMaster then we just fall through to OK
                if (machineName.Equals(System.Environment.MachineName))
                {
                    // ClusterMaster exit
                    return WalkDirs(new DirectoryInfo(path));
                }
#if DEBUG
                else
                    Console.WriteLine("DEBUG: Local machine '" + System.Environment.MachineName + "' is not cluster master '" + machineName + "'");
#endif

            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR: Unable to access path " + path + ": " + e.Message);
                return NAGIOS_CRITICAL;
            }

            // Non ClusterMaster exit
            return NAGIOS_OK;
        }

        /* Exmaple ServiceActivity.sal File --
         SPSBSAPS1P               Node of last activity
         2011-09-28 11:57:28      Timestamp of last activity
         WORKING                  Name of last activity
         900                      Threshold latency for Warning in NAGIOS
         1800                     Threshold latency for Error in NAGIOS
         -- */
        static int WalkDirs(DirectoryInfo root)
        {
#if DEBUG
            Console.WriteLine("DEBUG: Starting directory walk at " + root.FullName);
#endif
            DirectoryInfo[] subDirs = root.GetDirectories();
            if (subDirs != null && subDirs.Length > 0)
            {
                ret = NAGIOS_OK;
                foreach (DirectoryInfo dir in subDirs)
                {
#if DEBUG
                    Console.WriteLine("DEBUG: Loading all .sal files from " + dir.Name);
#endif
                    FileInfo[] files = dir.GetFiles("*.sal");
                    ProcessFiles(dir.Name, files);
                }

                try
                {
                    critMessages.Sort(new MessageComparer());
                    warnMessages.Sort(new MessageComparer());
                }
                catch (Exception e)
                {
                    Console.WriteLine("ERROR: Unable to sort: " + e.Message);
                    return NAGIOS_CRITICAL;
                }

#if DEBUG
                Console.WriteLine("DEBUG: Process file ret " + ret + " with " + critMessages.Count + " errors and " + warnMessages.Count + " warnings");
#endif

                if (ret == NAGIOS_CRITICAL)
                    Console.WriteLine(((Message)critMessages[0]).msg + (critMessages.Count > 1
                        ? " (" + (critMessages.Count - 1) + " more error" + (critMessages.Count > 2 ? "s" : "") + ")" : "")); // 001BC
                else if (ret == NAGIOS_WARNING)
                    Console.WriteLine(((Message)warnMessages[0]).msg + (warnMessages.Count > 1
                        ? " (" + (warnMessages.Count - 1) + " more warning" + (warnMessages.Count > 2 ? "s" : "") + ")" : "")); // 001BC
            }
#if DEBUG
            else
                Console.WriteLine("DEBUG: No Sub-Dirs found.");
#endif

            return ret;
        }

        static void ProcessFiles(string dirName, FileInfo[] files)
		{
#if DEBUG
            Console.WriteLine("DEBUG: Processing .sal files for " + dirName);
#endif
            if (files != null && files.Length > 0)
            {
                foreach (FileInfo fi in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(fi.Name);
#if DEBUG
                    Console.WriteLine("DEBUG: Processing " + fileName);
#endif
                    string[] lines = Regex.Split(fi.OpenText().ReadToEnd(), "\r\n|\n");

                    string hbTimeStamp = lines[1];
                    double timeDiff = (DateTime.Parse(DateTime.Now.ToString("yyyy'-'MM'-'dd HH:mm:ss")) - DateTime.Parse(hbTimeStamp.Trim())).TotalSeconds;

#if DEBUG
                    Console.WriteLine("DEBUG: File time " + hbTimeStamp + " diff from current time " + timeDiff + " error after " + double.Parse(lines[4]) + " warn after " + double.Parse(lines[3]));
#endif

                    string fmt = dirName + (fileName.Equals("ServiceActivityLog") ? "" : "/" + fileName)
                        + " HeartBeat {0} : Last Heartbeat " + hbTimeStamp + " on " + machineName;

                    if (timeDiff > double.Parse(lines[4]))
                    {
#if DEBUG
                        Console.WriteLine("DEBUG: Generating Nagios Error for " + fileName);
#endif
                        critMessages.Add(new Message(string.Format(fmt, "Error"), timeDiff));
                        ret = NAGIOS_CRITICAL;
                    }
                    else if (timeDiff > double.Parse(lines[3]))
                    {
#if DEBUG
                        Console.WriteLine("DEBUG: Generating Nagios Warning for " + fileName);
#endif
                        warnMessages.Add(new Message(string.Format(fmt, "Warning"), timeDiff));
                        if (ret == NAGIOS_OK)
                            ret = NAGIOS_WARNING;
                    }
                }
            }
#if DEBUG
            else
                Console.WriteLine("DEBUG: No .sal files found in " + dirName);
#endif
		}
	}
}
