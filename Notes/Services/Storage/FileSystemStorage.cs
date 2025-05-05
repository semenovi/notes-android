using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Notes.Services.Storage
{
    public class FileSystemStorage
    {
        private readonly string _rootPath;

        public string RootPath => _rootPath;

        public FileSystemStorage(string rootFolderName)
        {
            _rootPath = Path.Combine(FileSystem.AppDataDirectory, rootFolderName);
            EnsureFolderExists(_rootPath);
        }

        public async Task<byte[]> ReadFileAsync(string path)
        {
            string fullPath = GetFullPath(path);
            if (!File.Exists(fullPath))
                return null;

            return await File.ReadAllBytesAsync(fullPath);
        }

        public async Task WriteFileAsync(string path, byte[] data)
        {
            string fullPath = GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            EnsureFolderExists(directory);

            await File.WriteAllBytesAsync(fullPath, data);
        }

        public bool DeleteFile(string path)
        {
            string fullPath = GetFullPath(path);
            if (!File.Exists(fullPath))
                return false;

            File.Delete(fullPath);
            return true;
        }

        public List<string> GetFiles(string directory)
        {
            string fullPath = GetFullPath(directory);
            if (!Directory.Exists(fullPath))
                return new List<string>();

            var files = Directory.GetFiles(fullPath);
            return files.Select(f => Path.GetRelativePath(_rootPath, f)).ToList();
        }

        public List<string> GetDirectories(string directory)
        {
            string fullPath = GetFullPath(directory);
            if (!Directory.Exists(fullPath))
                return new List<string>();

            var directories = Directory.GetDirectories(fullPath);
            return directories.Select(d => Path.GetRelativePath(_rootPath, d)).ToList();
        }

        public bool CreateDirectory(string directory)
        {
            string fullPath = GetFullPath(directory);
            if (Directory.Exists(fullPath))
                return false;

            Directory.CreateDirectory(fullPath);
            return true;
        }

        public bool DeleteDirectory(string directory, bool recursive = false)
        {
            string fullPath = GetFullPath(directory);
            if (!Directory.Exists(fullPath))
                return false;

            Directory.Delete(fullPath, recursive);
            return true;
        }

        private string GetFullPath(string relativePath)
        {
            return Path.Combine(_rootPath, relativePath);
        }

        private void EnsureFolderExists(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
    }
}