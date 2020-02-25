using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IO;
using System.Diagnostics;

namespace cabtool
{
    // Wrap makecab.exe and expand.exe
    class Program
    {
        private static CCmdParser m_parser;
        private const int MAX_LINES_IN_DDF = 65534;
        private const int DDF_HEADER_LINES = 8;
        private static StringBuilder m_txtOutput = new StringBuilder();
        private static string m_srcDir = "";
        private static List<string> m_fileExcludes = new List<string>();
        private static List<string> m_dirExcludes = new List<string>();

        static int Main(string[] args)
        {
            Dictionary<String, KeyValuePair<EArgType, bool>> argList = new Dictionary<String, KeyValuePair<EArgType, bool>>();
            argList.Add("-source", new KeyValuePair<EArgType, bool>(EArgType.VALUE, true));
            argList.Add("-target", new KeyValuePair<EArgType, bool>(EArgType.VALUE, false));
            argList.Add("-exf", new KeyValuePair<EArgType, bool>(EArgType.VALUE, false));
            argList.Add("-exd", new KeyValuePair<EArgType, bool>(EArgType.VALUE, false));
            argList.Add("-a", new KeyValuePair<EArgType, bool>(EArgType.FLAG, false));
            argList.Add("-x", new KeyValuePair<EArgType, bool>(EArgType.FLAG, false));
            m_parser = new CCmdParser(argList);
            if (!m_parser.Parse(args))
            {
                foreach (String arg in m_parser.Options.Keys)
                {
                    Console.WriteLine(arg + " = " + m_parser.Options[arg]);
                }
                Usage();
                return 1;
            }
            bool add = false;
            bool extract = false;
            if (!string.IsNullOrEmpty(m_parser.Options["-a"]))
                add = true;
            if (!string.IsNullOrEmpty(m_parser.Options["-x"]))
                extract = true;
            if (add == extract)
            {
                if (add)
                    Console.WriteLine("Cannot specify both -a and -x");
                else
                    Console.WriteLine("Must specify -a or -x");
                Usage();
                return 2;
            }
            string source = m_parser.Options["-source"];
            string target = "";
            if (!string.IsNullOrEmpty(m_parser.Options["-target"]))
                target = m_parser.Options["-target"];
            if(!string.IsNullOrEmpty(m_parser.Options["-exf"]))
            {
                string[] parts = m_parser.Options["-exf"].Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                m_fileExcludes = new List<string>(parts);
            }
            if (!string.IsNullOrEmpty(m_parser.Options["-exf"]))
            {
                string[] parts = m_parser.Options["-exf"].Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                m_dirExcludes = new List<string>(parts);
            }
            if (add)
            {
                int exit = createCabinet(source, target);
                if (exit > 0)
                    Console.Write(m_txtOutput.ToString());
                else
                    Console.WriteLine("Cabinet created.");
                return exit;
            }
            else
            {
                int exit = extractCabinet(source, target);
                if (exit > 0)
                    Console.Write(m_txtOutput.ToString());
                else
                    Console.WriteLine("Cabinet extracted.");
                return exit;
            }
        }

        private static int createCabinet(String sourceFolder, String targetFile = "")
        {
            int exitcode = 0;
            m_txtOutput = new StringBuilder();
            string filename = targetFile;
            string targetFolder = "";
            if(string.IsNullOrEmpty(targetFile))
                targetFolder = "";
            else
                targetFolder = Path.GetDirectoryName(targetFile);
            if (String.IsNullOrEmpty(filename))
                filename = Path.GetFileName(sourceFolder) + ".cab";
            if (String.IsNullOrWhiteSpace(sourceFolder) ||
                String.IsNullOrWhiteSpace(filename))
            {
                m_txtOutput.Append("Error: Source path must be specified.");
                exitcode = 4;
            }
            else
            {
                try
                {
                    // Create target folder if it doesn't already exist

                    if (!string.IsNullOrEmpty(targetFolder) && !Directory.Exists(targetFolder))
                    {
                        Directory.CreateDirectory(targetFolder);
                    }

                    // Build DDF file
                    m_srcDir = sourceFolder;
                    List<DdfFileRow> ddfFiles = GetFiles(sourceFolder);
                    Dictionary<int, List<DdfFileRow>> ddfDictionary = getFileCollection(ddfFiles);
                    foreach (int key in ddfDictionary.Keys)
                    {
                        string ddfPath = "";
                        if(key == 0)
                            ddfPath = writeDDF(sourceFolder, targetFolder, filename, ddfDictionary[key]);
                        else
                        {
                            string fname = string.Format("{0}{1}.cab", Path.GetFileName(sourceFolder), key);
                            ddfPath = writeDDF(sourceFolder, targetFolder, Path.GetFileName(sourceFolder), ddfDictionary[key], key);
                        }

                        string cmd = String.Format("/f {0}", ddfPath);

                        // Run "makecab.exe"

                        Process process = new Process();

                        ProcessStartInfo startInfo = new ProcessStartInfo();
                        startInfo.CreateNoWindow = true;
                        startInfo.FileName = "makecab.exe";
                        startInfo.Arguments = cmd;
                        startInfo.RedirectStandardOutput = true;
                        startInfo.RedirectStandardError = true;
                        startInfo.UseShellExecute = false;
                        process.StartInfo = startInfo;

                        process.ErrorDataReceived += Process_ErrorDataReceived;
                        process.OutputDataReceived += Process_OutputDataReceived;

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();
                        exitcode = process.ExitCode;

                        m_txtOutput.Append("Exit code: " + process.ExitCode + Environment.NewLine);

                        if (process.ExitCode == 0)
                        {
                            File.SetLastWriteTime(Path.Combine(targetFolder, filename), DateTime.Now);
                        }
                        else
                        {
                            m_txtOutput.Append("CAB file not created. See output for details.");
                        }
                        File.Delete(ddfPath);
                    }
                }
                catch (Exception ex)
                {
                    m_txtOutput.Append("CAB file not created.");

                    m_txtOutput.Append("Error: " + ex.ToString());
                }
            }
            return exitcode;
        }

        static string writeDDF(string sourceFolder, string targetFolder, string filename, List<DdfFileRow> ddfFiles, int index = 0)
        {
            string txtFileName = Path.GetFileNameWithoutExtension(filename) + ".ddf";
            if (index > 0)
                txtFileName = string.Format("{0}{1}.ddf", Path.GetFileNameWithoutExtension(filename), index);
            string ddfPath = Path.Combine(targetFolder, txtFileName);

            StringBuilder ddf = new StringBuilder();
            ddf.AppendFormat(@";*** MakeCAB Directive file;
.OPTION EXPLICIT
.Set CabinetNameTemplate={0}
.Set DiskDirectory1={1}
.Set MaxDiskSize=0
.Set Cabinet=on
.Set Compress=on
", filename, targetFolder);

            int ddfHeaderLines = ddf.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length;
            int maxFiles = MAX_LINES_IN_DDF - ddfHeaderLines; // only write enough files to hit the max # of lines allowed in a DDF (blank lines don't count)
            m_srcDir = sourceFolder;
            foreach (var ddfFile in ddfFiles.Take(maxFiles))
            {
                ddf.AppendFormat("\"{0}\" \"{1}\"{2}", ddfFile.FullName, ddfFile.Path, Environment.NewLine);
            }

            File.WriteAllText(ddfPath, ddf.ToString(), Encoding.Default);

            return ddfPath;
        }

        static Dictionary<int, List<DdfFileRow>> getFileCollection(List<DdfFileRow> files)
        {
            Queue<DdfFileRow> fqueue = new Queue<DdfFileRow>(files);
            Dictionary<int, List<DdfFileRow>> ddfDictionary = new Dictionary<int, List<DdfFileRow>>();
            int maxFiles = MAX_LINES_IN_DDF - DDF_HEADER_LINES;
            int index = 0;
            while(fqueue.Count > 0)
            {
                DdfFileRow row = fqueue.Dequeue();
                if (!ddfDictionary.ContainsKey(index))
                {
                    List<DdfFileRow> list = new List<DdfFileRow>();
                    list.Add(row);
                    ddfDictionary.Add(index, list);
                }
                else
                    ddfDictionary[index].Add(row);
                if (ddfDictionary[index].Count > maxFiles)
                    ++index;
            }
            return ddfDictionary;
        }

        static int extractCabinet(String cabinetPath, String targetPath = "")
        {
            int exitcode = 0;
            m_txtOutput = new StringBuilder();
            string filename = cabinetPath;
            if (String.IsNullOrWhiteSpace(filename))
            {
                m_txtOutput.Append("Error: Source path must be specified.");
                exitcode = 8;
            }
            else if(!File.Exists(filename))
            {
                m_txtOutput.Append(string.Format("Error: CAB file {0} not found.", filename));
                exitcode = 16;
            }
            else
            {
                try
                {
                    // Create target folder if it doesn't already exist

                    if (string.IsNullOrEmpty(targetPath))
                        targetPath = string.Format(".\\{0}\\", Path.GetFileNameWithoutExtension(filename));
                    if (!Directory.Exists(targetPath))
                    {
                        Directory.CreateDirectory(targetPath);
                    }

                    string cmd = String.Format("/r {0} /f:* {1}", cabinetPath, targetPath);

                    // Run "makecab.exe"

                    Process process = new Process();

                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.CreateNoWindow = true;
                    startInfo.FileName = "expand.exe";
                    startInfo.Arguments = cmd;
                    startInfo.RedirectStandardOutput = true;
                    startInfo.RedirectStandardError = true;
                    startInfo.UseShellExecute = false;
                    process.StartInfo = startInfo;

                    process.ErrorDataReceived += Process_ErrorDataReceived;
                    process.OutputDataReceived += Process_OutputDataReceived;

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    exitcode = process.ExitCode;

                    m_txtOutput.Append("Exit code: " + process.ExitCode + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    m_txtOutput.Append("CAB file not extracted.");

                    m_txtOutput.Append("Error: " + ex.ToString());
                }
            }
            return exitcode;
        }

        // capture output from console stdout to output box on form
        private static void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                m_txtOutput.Append(e.Data + Environment.NewLine);
            }
        }

        // capture output from console stderr to output box on form
        private static void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                m_txtOutput.Append(e.Data + Environment.NewLine);
            }
        }

        private static List<DdfFileRow> GetFiles(string sDir)
        {
            List<DdfFileRow> list = new List<DdfFileRow>();
            bool recurse = false;
            if (Directory.Exists(sDir))
                recurse = true;
            string srcPath = sDir;
            if (File.Exists(sDir))
            {
                list.Add(new DdfFileRow()
                {
                    FullName = Path.GetDirectoryName(sDir),
                    Path = sDir
                });
            }
            else
            {
                foreach (string f in Directory.GetFiles(sDir))
                {
                    bool skip = false;
                    foreach(string e in m_fileExcludes)
                    {
                        if(Path.GetFileName(f).Contains(e))
                        {
                            skip = true;
                            break;
                        }
                    }
                    if (skip)
                        continue;
                    list.Add(new DdfFileRow()
                    {
                        FullName = f,
                        Path = f.Replace(m_srcDir, "").TrimStart('\\')
                    });
                }

                if (recurse)
                {
                    foreach (string d in Directory.GetDirectories(sDir))
                    {
                        bool skip = false;
                        foreach (string dir in m_dirExcludes)
                        {
                            if (Path.GetFileName(d).Contains(dir))
                            {
                                skip = true;
                                break;
                            }
                        }
                        if (skip)
                            continue;
                        list.AddRange(GetFiles(d));
                    }
                }
            }
            return list;
        }

        static void Usage()
        {
            Console.WriteLine("-source  required - source file or folder to add");
            Console.WriteLine("-target  (optional) name of cab file if adding, target folder for extracting");
            Console.WriteLine("-a       add file(s) or a folder.");
            Console.WriteLine("-x       extract file(s) to -target, or to the current folder if no target");
            Console.WriteLine("");
            Console.WriteLine("Must provide -a or -x, and a -source");
        }
    }
    public class DdfFileRow
    {
        public string FullName { get; set; }
        public string Path { get; set; }
    }
}
