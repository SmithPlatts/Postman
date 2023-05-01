using System;
using System.IO;
using System.Runtime.InteropServices;
using EnvDTE80;

namespace Postman.Common
{
    public class Setup
    {
        readonly string gitRootFolder;

        public Setup()
        {
            if (!IsTestAgentRun) gitRootFolder = GetGitRootFolder(SolutionDirectory);
        }

        public string SolutionDirectory
        {
            get
            {
                DTE2 dte = DTEHandler.GetCurrent();

                string solutionDirectory = string.Empty;
                int retry = 0;
                do
                {
                    try
                    {
                        solutionDirectory = Path.GetDirectoryName(dte.Solution.FullName);
                    }
                    catch (COMException ex)
                    {
                        if (ex.HResult == -2147417846) // 0x8001010A (RPC_E_SERVERCALL_RETRYLATER)
                        {
                            retry++;
                            System.Threading.Thread.Sleep(500);
                            continue;
                        }

                        throw;
                    }
                }
                while (string.IsNullOrWhiteSpace(solutionDirectory) && retry > 0 && retry <= 5);

                return solutionDirectory;
            }
        }

        public bool IsTestAgentRun
        {
            get
            {
                return !string.IsNullOrEmpty(SystemWorkFolder);
            }
        }

        public string SystemWorkFolder
        {
            get
            {
                return Environment.GetEnvironmentVariable("System_WorkFolder");
            }
        }

        public string GitRootFolder
        {
            get
            {
                return gitRootFolder;
            }
        }

        public string CollectionFileNamePattern
        {
            get
            {
                return "*.postman_collection.json";
            }
        }

        public string[] GetCollectionFilePaths()
        {
            string searchFolder = IsTestAgentRun ? SystemWorkFolder : gitRootFolder;
            string[] files = Directory.GetFiles(searchFolder, CollectionFileNamePattern, SearchOption.AllDirectories);
            return files;
        }

        public string GetGitRootFolder(string fromPath)
        {
            DirectoryInfo rootDir = new DirectoryInfo(fromPath);
            do
            {
                DirectoryInfo gitDir = new DirectoryInfo(Path.Combine(rootDir.FullName, ".git"));
                if (gitDir.Exists) break;
                if (rootDir.Parent == null) throw new Exception("Git root folder could not be found - no parent folder");
                rootDir = rootDir.Parent;
            }
            while (rootDir != null);

            Console.WriteLine("Git root folder : " + rootDir.FullName);

            return rootDir.FullName;
        }
    }
}
