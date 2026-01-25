using System;
using System.IO;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

namespace TestPerf
{
    class Program
    {
        static void Main(string[] args)
        {
            string path = @"c:\workspace\LargeFolderFinder";
            // If workspace is small, use System32 but cap count
            if (!Directory.Exists(path)) path = @"C:\Windows\System32";

            Console.WriteLine($"Testing on: {path}");
            var files = Directory.GetFiles(path);
            int count = Math.Min(files.Length, 500); // Test 500 files
            if (count == 0)
            {
                Console.WriteLine("No files found.");
                return;
            }

            Console.WriteLine($"Target count: {count}");

            // Warmup
            foreach (var f in files) { var fi = new FileInfo(f); var s = fi.Length; if (--count <= 0) break; }
            count = Math.Min(files.Length, 500);

            // Test 1: Basic Info
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                var fi = new FileInfo(files[i]);
                var n = fi.Name;
                var s = fi.Length;
                var t = fi.LastWriteTime;
            }
            sw.Stop();
            Console.WriteLine($"Basic Info: {sw.ElapsedMilliseconds} ms ({sw.ElapsedMilliseconds / (double)count} ms/file)");

            // Test 2: With Owner
            sw.Restart();
            for (int i = 0; i < count; i++)
            {
                var fi = new FileInfo(files[i]);
                var n = fi.Name;
                var s = fi.Length;
                var t = fi.LastWriteTime;
                try
                {
                    var fs = fi.GetAccessControl();
                    var owner = fs.GetOwner(typeof(NTAccount));
                    var ownerName = owner?.ToString();
                }
                catch { }
            }
            sw.Stop();
            Console.WriteLine($"With Owner: {sw.ElapsedMilliseconds} ms ({sw.ElapsedMilliseconds / (double)count} ms/file)");
        }
    }
}
