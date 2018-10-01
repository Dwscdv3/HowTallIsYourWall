using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace HowTallIsYourWall
{
    [Command(Name = "HowTallIsYourWall", Description = "A network availability benchmark tool.")]
    [HelpOption]
    class Program
    {
        [Option("-n|--threads", Description = "Number of running threads, default to 8. A high value may lead to inaccurate results.")]
        int RunningTasks { get; } = 8;

        [Option("-t|--timeout", Description = "Connection timeout in milliseconds, default to 3000.")]
        int Timeout { get; } = 3000;

        [Argument(0, "The domain list file to use.")]
        string DomainListFile { get; }

        string[] DomainList;
        IEnumerator<string> DomainListEnumerator;
        static readonly object DomainListEnumeratorLock = new object();
        static readonly object IOLock = new object();

        int Passed = 0;
        int Failed = 0;
        int Finished => Passed + Failed;
        double PassRate => (double)Passed / Finished;

        string LogFile;

        static int Main(string[] args) => CommandLineApplication.Execute<Program>(args);

        void OnExecute()
        {
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            DomainList = File.ReadAllLines(DomainListFile ?? SelectDomainListFile());
            DomainListEnumerator = ((IEnumerable<string>)DomainList).GetEnumerator();

            Directory.CreateDirectory("Log");
            LogFile = $"Log/{DateTimeOffset.Now.ToUnixTimeSeconds()}.log";

            for (var i = 0; i < RunningTasks; i++)
                RunTask();

            Console.WriteLine();

            while (Finished < DomainList.Length)
            {
                Thread.Sleep(500);
                Console.Write($"\r{Finished} / {DomainList.Length} finished, Pass rate {PassRate * 100:F0}%");
            }

            Console.WriteLine("\nDone");
            Console.ReadKey(true);
        }

        void RunTask()
        {
            Task.Run<(string domain, bool? pass)>(() =>
            {
                var domain = NextDomain();
                if (domain is null) return (domain, null);
                else
                {
                    try
                    {
                        //new TcpClient(domain, 443);
                        if (!new TcpClient().ConnectAsync(domain, 443).Wait(Timeout))
                        {
                            throw new TimeoutException();
                        }
                    }
                    catch
                    {
                        try
                        {
                            //new TcpClient(domain, 80);
                            if (!new TcpClient().ConnectAsync(domain, 80).Wait(Timeout))
                            {
                                throw new TimeoutException();
                            }
                        }
                        catch { return (domain, false); }
                    }
                    return (domain, true);
                }
            })
            .ContinueWith(task =>
            {
                var (domain, pass) = task.Result;
                if (pass == true) Passed += 1;
                else if (pass == false) Failed += 1;
                if (domain != null)
                {
                    Log(domain, pass.Value);
                    RunTask();
                }
            });
        }

        void Log(string domain, bool pass)
        {
            lock (IOLock)
            {
                File.AppendAllText(LogFile, $"{(pass ? "✓" : "✗")} {domain}\r\n");
            }
        }

        string NextDomain()
        {
            lock (DomainListEnumeratorLock)
            {
                if (DomainListEnumerator.MoveNext()) return DomainListEnumerator.Current;
                else return null;
            }
        }

        string SelectDomainListFile()
        {
            var domainListFiles = Directory.GetFiles("DomainLists", "*.txt");
            PrintDomainLists(domainListFiles);
            while (true)
            {
                Console.Write("Choose a list: ");
                if (int.TryParse(Console.ReadLine(), out var index) &&
                    index > 0 && index <= domainListFiles.Length)
                    return domainListFiles[index - 1];
                else
                    Console.WriteLine("Not a valid number. Try again.");
            }
        }
        void PrintDomainLists(IEnumerable<string> domainListFiles)
        {
            var count = 0;
            foreach (var domainList in domainListFiles)
            {
                count += 1;
                Console.WriteLine($"{count}\t{Path.GetFileName(domainList)}");
            }
        }
    }
}
