using System;
using System.Collections.Generic;
using System.Linq;

namespace Notes.Services.Markdown
{
    public class SyntaxExtensionManager
    {
        private readonly List<ISyntaxExtension> _extensions;

        public SyntaxExtensionManager()
        {
            _extensions = new List<ISyntaxExtension>();
        }

        public void RegisterExtension(ISyntaxExtension extension)
        {
            if (extension == null)
                throw new ArgumentNullException(nameof(extension));

            if (_extensions.Any(e => e.Name == extension.Name))
                throw new InvalidOperationException($"Extension with name '{extension.Name}' is already registered");

            _extensions.Add(extension);
        }

        public ISyntaxExtension GetExtension(string name)
        {
            return _extensions.FirstOrDefault(e => e.Name == name);
        }

        public List<ISyntaxExtension> GetAllExtensions()
        {
            return _extensions.ToList();
        }

        public bool RemoveExtension(string name)
        {
            var extension = GetExtension(name);
            if (extension != null)
            {
                return _extensions.Remove(extension);
            }
            return false;
        }
    }
}