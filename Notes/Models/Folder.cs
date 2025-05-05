using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Notes.Models
{
    public class Folder : INotifyPropertyChanged
    {
        private string _id;
        private string _name;
        private string _parentId;
        private ObservableCollection<Note> _notes;

        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string ParentId
        {
            get => _parentId;
            set => SetProperty(ref _parentId, value);
        }

        public ObservableCollection<Note> Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        public Folder()
        {
            Id = Guid.NewGuid().ToString();
            Notes = new ObservableCollection<Note>();
        }

        public void AddNote(Note note)
        {
            note.FolderId = Id;
            Notes.Add(note);
        }

        public void RemoveNote(string noteId)
        {
            var noteToRemove = Notes.FirstOrDefault(n => n.Id == noteId);
            if (noteToRemove != null)
            {
                Notes.Remove(noteToRemove);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}