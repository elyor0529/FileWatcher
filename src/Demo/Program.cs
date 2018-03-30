using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Timers;

namespace Demo
{

    /// <summary>
    /// Common methods
    /// </summary>
    public static class Helpers
    {

        /// <summary>
        /// http://edndoc.esri.com/arcobjects/9.2/net/shared/geoprocessing/sharing_tools_and_toolboxes/pathnames_explained_colon_absolute_relative_unc_and_url.htm
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static bool IsFullPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                    && path.IndexOfAny(Path.GetInvalidPathChars().ToArray()) == -1
                    && Path.IsPathRooted(path)
                    && !Path.GetPathRoot(path).Equals(Path.DirectorySeparatorChar.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Check single extension
        /// </summary>
        /// <param name="ext"></param>
        /// <returns></returns>
        public static bool CheckFileExtension(string ext)
        {
            var prefix = "*.";

            return !string.IsNullOrWhiteSpace(ext)
                && ext.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && ext.Split(new string[] { prefix }, StringSplitOptions.RemoveEmptyEntries).Length == 1;
        }

        /// <summary>
        /// Count the number of lines in the file specified.
        /// </summary>
        /// <param name="file">The filename to count lines.</param>
        /// <returns>The number of lines in the file.</returns>

        public static long CountLines(string file)
        {
            if (!File.Exists(file))
                return 0;

            long counter = 0;

            using (var reader = new StreamReader(file, Encoding.UTF8))
            {
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    counter++;
                }
            }

            return counter;
        }

    }

    /// <summary>
    /// Generic event watcher
    /// </summary>
    /// <param name="path"></param>
    public delegate void FileSystemEvent(string path);

    /// <summary>
    /// Infrasutructure
    /// </summary>
    public interface IDirectoryWatcher : IDisposable
    {

        event FileSystemEvent Change;

        void Start();

    }

    /// <summary>
    /// https://msdn.microsoft.com/en-us/library/system.io.filesystemwatcher(v=vs.110).aspx
    /// </summary>
    public class DirectoryWatcher : IDirectoryWatcher
    {


        #region Consts

        private const int TriggerEvery = 10 * 1000;

        #endregion

        #region Static

        private static List<string> FindPaths(Dictionary<string, DateTime> entries)
        {
            var results = new List<string>();
            var now = DateTime.Now;

            foreach (var entry in entries)
            {
                // If the path has not received a new event in the last 50ms an event for the path should be fired
                var diff = now.Subtract(entry.Value).TotalMilliseconds;
                if (diff >= 50)
                {
                    results.Add(entry.Key);
                }
            }

            return results;
        }


        #endregion

        #region Fields

        private readonly Dictionary<string, DateTime> _pendings = new Dictionary<string, DateTime>();
        private FileSystemWatcher _watcher;
        private Timer _timer;
        private bool _started;

        #endregion

        #region Ctors

        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="dir">Root directory</param>
        /// <param name="ext">Filter extension</param>
        public DirectoryWatcher(string dir, string ext)
        {
            // Create a new FileSystemWatcher and set its properties.
            _watcher = new FileSystemWatcher();
            _watcher.Path = dir;
            _watcher.Filter = ext;
            _watcher.IncludeSubdirectories = true;

            /* Watch for changes in LastAccess and LastWrite times, and the renaming of files or directories. */
            _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.DirectoryName;

            // Add event handlers.
            _watcher.Created += Watcher_Created;
            _watcher.Renamed += Watcher_Renamed;
            _watcher.Deleted += Watcher_Deleted;
            _watcher.Changed += Watcher_Changed;
            _watcher.Error += Watcher_Error;

            //timer instance initialize
            _timer = new Timer
            {
                AutoReset = true,
                Enabled = true,
                Interval = TriggerEvery
            };
            _timer.Elapsed += Timer_Elapsed;
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            List<string> paths;

            // Don't want other threads messing with the pending events right now
            lock (_pendings)
            {
                // Get a list of all paths that should have events thrown
                paths = FindPaths(_pendings);

                // Remove paths that are going to be used now
                paths.ForEach((path) =>
                {
                    _pendings.Remove(path);
                });

                // Stop the timer if there are no more events pending
                if (_pendings.Count == 0)
                {
                    _timer.Stop();
                    _started = false;
                }
            }

            // Fire an event for each path that has changed
            paths.ForEach((path) =>
            {
                Change?.Invoke(path);
            });
        }

        private void Watcher_Error(object sender, ErrorEventArgs e)
        {
            var ext = e.GetException();

            if (ext == null || string.IsNullOrWhiteSpace(ext.Message))
                return;

            Console.WriteLine(ext.Message);
        }

        #endregion

        #region Events

        public event FileSystemEvent Change;

        #endregion

        #region Public

        public void Start()
        {
            // Begin watching.
            _watcher.EnableRaisingEvents = true;
        }

        #endregion

        #region Private

        /// <summary>
        ///  FullPath is the location of where the file used to be.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Watcher_Deleted(object sender, FileSystemEventArgs e)
        {
            // Don't want other threads messing with the pending events right now
            lock (_pendings)
            {
                //remove to path
                if (_pendings.ContainsKey(e.FullPath))
                    _pendings.Remove(e.FullPath);
            }

            Console.WriteLine("File: {0} deleted.", e.FullPath);
        }

        /// <summary>
        // FullPath is the new file's path.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Watcher_Created(object sender, FileSystemEventArgs e)
        {
            // Don't want other threads messing with the pending events right now
            lock (_pendings)
            {
                //add to path
                _pendings.Add(e.FullPath, DateTime.Now);
            }

            //if file copy/past
            var lines = Helpers.CountLines(e.FullPath);

            Console.WriteLine("File: {0} created({1} lines).", e.FullPath, lines);
        }

        /// <summary>
        /// FullPath is the new file name.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Watcher_Renamed(object sender, RenamedEventArgs e)
        {
            // Don't want other threads messing with the pending events right now
            lock (_pendings)
            {
                //replace to path
                _pendings.Remove(e.OldFullPath);
                _pendings.Add(e.FullPath, DateTime.Now);
            }

            Console.WriteLine("File: {0} renamed to {1}.", e.OldFullPath, e.FullPath);
        }

        /// <summary>
        ///  Occurs when the contents of the file change.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            // Don't want other threads messing with the pending events right now
            lock (_pendings)
            {
                // Save a timestamp for the most recent event for this path
                _pendings[e.FullPath] = DateTime.Now;

                // Start a timer if not already started
                if (!_started)
                {
                    _timer.Start();
                    _started = true;
                }
            }
        }

        public void Dispose()
        {
            if (_started)
            {
                _started = false;
            }

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }

            if (_pendings.Count > 0)
            {
                _pendings.Clear();
            }

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }

            GC.SuppressFinalize(this);
        }

        #endregion

    }

    internal class Program
    {
        private static DirectoryWatcher _monitoring;

        [STAThread]
        private static void Main(string[] args)
        {
            // If a directory is not specified, exit program.
            if (args.Length != 2)
            {
                // Display the proper way to call the program.
                Console.WriteLine("Usage:  FileWatcher.Demo.exe \"c:\file folder\" *.txt");

                return;
            }

            var dir = args[0];

            if (!Helpers.IsFullPath(dir))
            {
                Console.WriteLine("Directory: {0} path is not valid.", dir);

                return;
            }

            var ext = args[1];

            if (!Helpers.CheckFileExtension(ext))
            {
                Console.WriteLine("Extension: {0} not valid.", ext);

                return;
            }

            Run(dir, ext);

            // Wait for the user to quit the program.
            Console.WriteLine("Press \'q\' to quit the Demo.");

            //waiting to response
            while (true)
            {
                if (Console.Read() == 'q')
                {
                    _monitoring.Dispose();

                    break;
                }

                System.Threading.Thread.Sleep(100);
            }
        }

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        private static void Run(string dir, string ext)
        {
            _monitoring = new DirectoryWatcher(dir, ext);
            _monitoring.Change += Monitor_Change;
            _monitoring.Start();
        }

        private static void Monitor_Change(string path)
        {
            var lines = Helpers.CountLines(path);

            Console.WriteLine("File: {0} changed ({1} lines).", path, lines);
        }

    }
}
