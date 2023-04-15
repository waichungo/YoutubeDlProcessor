using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YoutubeDlProcessor
{
    class Program
    {
        static List<string> uris = new List<string>();

        private static Mutex FinishedLock = new Mutex();
        private static Mutex FinishedWriteLock = new Mutex();
        private static Mutex ConsoleLock = new Mutex();
        private static volatile int Position = 0;
        private static volatile bool HasFinished = false;
        private static volatile int Finished = 0;

        public static ProcessStartInfo GetInfo(string arg)
        {
            return new ProcessStartInfo
            {
                FileName = "youtube-dl.exe",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                Arguments = arg,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
        }

        [STAThread]
        static void Main(string[] args)
        {
            var name = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
            var appLock = new Mutex(false, name);
            if (appLock.WaitOne(TimeSpan.Zero))
            {
                FileSystemWatcher watcher = new FileSystemWatcher(".", "*.txt");
                watcher.Created += ProcessFileChangeEvents;
                watcher.Changed += ProcessFileChangeEvents;
                var files = Directory.GetFiles(".", "*.txt");
                foreach (var file in files)
                {
                    uris.AddRange(File.ReadAllLines(file).Where(e => e.ToLower().StartsWith("http")));
                }
                uris = uris.Distinct().ToList();
                if (File.Exists("finished.text"))
                {
                    try
                    {
                        File.ReadAllLines("finished.text").ToList().ForEach(e =>
                        {
                            uris.Remove(e);
                        });
                    }
                    catch (Exception)
                    {

                    }

                }
                watcher.EnableRaisingEvents = true;
                Run();
                Application.Run();
            }
        }

        private static void Run()
        {
            List<Task> jobs = new List<Task>();

            do
            {
                jobs = jobs.Where(e => !e.IsCompleted).ToList();
                var total = uris.Count;
                try
                {
                    FinishedLock.WaitOne();
                    int remaining = uris.Count() - Finished;
                    total = remaining < 3 ? remaining : 3 - jobs.Where(e => !e.IsCompleted).Count();

                }
                catch { }
                finally
                {
                    FinishedLock.ReleaseMutex();
                }
                if (uris.Count() == 0 || Position == uris.Count()) break;
                for (int i = 0; i < total; i++)
                {
                    var uri = uris[Position];
                    Position++;
                    var task = Task.Factory.StartNew(() =>
                     {
                         try
                         {
                             var info = GetInfo(uri);
                             var proc = Process.Start(info);
                             proc.OutputDataReceived += Proc_OutputDataReceived;
                             proc.ErrorDataReceived += Proc_OutputDataReceived;
                             proc.BeginOutputReadLine();
                             proc.BeginErrorReadLine();
                             proc.WaitForExit();
                             try
                             {
                                 FinishedWriteLock.WaitOne();
                                 if (proc.ExitCode == 0) File.AppendAllText("finished.text", uri + Environment.NewLine);
                             }
                             finally
                             {
                                 FinishedWriteLock.ReleaseMutex();
                             }
                         }
                         catch (Exception)
                         {


                         }
                         try
                         {
                             FinishedLock.WaitOne();
                             Finished++;
                         }
                         finally
                         {
                             FinishedLock.ReleaseMutex();
                         }
                     });
                    jobs.Add(task);
                }
                try
                {
                    Console.Title = $"Position ({Position }/{uris.Count()}), {uris.Count() - (Position)} remaining, {jobs.Where(e => !e.IsCompleted).Count()} downloading";
                    Task.WaitAny(jobs.ToArray());

                }
                catch (Exception)
                {


                }


            } while (jobs.Count() > 0);
            HasFinished = true;
            if (jobs.Where(e => !e.IsCompleted).Count() > 0)
            {
                try
                {
                    Task.WaitAll(jobs.ToArray());
                }
                catch (Exception)
                {
                }

            }
           
            Console.WriteLine("Finished ..");
        }

        private static void Proc_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                try
                {
                    ConsoleLock.WaitOne();                 
                    Console.WriteLine(e.Data);
                    
                }
                finally
                {
                    ConsoleLock.ReleaseMutex();
                }
            }
        }

        private static void ProcessFileChangeEvents(object sender, FileSystemEventArgs evt)
        {
            while (true)
            {
                try
                {
                    var lines = File.ReadAllLines(evt.FullPath).Where(e => e.ToLower().StartsWith("http"));
                    lines.ToList().ForEach((e) =>
                    {
                        if (!uris.Contains(e)) uris.Add(e);
                    });
                    if (HasFinished)
                    {
                        Run();
                        HasFinished = false;
                        break;
                    }
                }
                catch (Exception)
                {


                }

                Thread.Sleep(300);
            }
        }
    }
}
