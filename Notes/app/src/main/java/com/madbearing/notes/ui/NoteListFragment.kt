package com.madbearing.notes.ui

import android.app.AlertDialog
import android.os.Bundle
import android.view.LayoutInflater
import android.view.Menu
import android.view.MenuInflater
import android.view.MenuItem
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.navigation.fragment.findNavController
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.madbearing.notes.R
import com.madbearing.notes.data.NoteStorage
import com.madbearing.notes.models.Note

class NoteListFragment : Fragment() {

    private lateinit var noteStorage: NoteStorage
    private lateinit var recyclerView: RecyclerView
    private lateinit var adapter: NoteAdapter

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?
    ): View? {
        val view = inflater.inflate(R.layout.fragment_note_list, container, false)
        noteStorage = NoteStorage(requireContext())
        recyclerView = view.findViewById(R.id.recycler_view)
        recyclerView.layoutManager = LinearLayoutManager(requireContext())
        adapter = com.madbearing.notes.ui.NoteAdapter(emptyList())
        recyclerView.adapter = adapter
        setHasOptionsMenu(true)
        return view
    }

    override fun onCreateOptionsMenu(menu: Menu, inflater: MenuInflater) {
        inflater.inflate(R.menu.menu_note_list, menu)
        super.onCreateOptionsMenu(menu, inflater)
    }

    override fun onOptionsItemSelected(item: MenuItem): Boolean {
        return when (item.itemId) {
            R.id.action_create_note -> {
                findNavController().navigate(R.id.action_noteListFragment_to_noteCreateFragment)
                true
            }
            R.id.action_delete_note -> {
                showDeleteConfirmationDialog()
                true
            }
            else -> super.onOptionsItemSelected(item)
        }
    }

    private fun showDeleteConfirmationDialog() {
        val alertDialog = AlertDialog.Builder(requireContext())
            .setTitle("Удалить заметку")
            .setMessage("Вы действительно хотите удалить выбранную заметку?")
            .setPositiveButton("Удалить") { _, _ ->
                deleteSelectedNote()
            }
            .setNegativeButton("Отмена", null)
            .create()
        alertDialog.show()
    }

    private fun deleteSelectedNote() {
        val selectedNotePosition = adapter.selectedNotePosition
        if (selectedNotePosition != -1) {
            val notes = noteStorage.loadNotes().toMutableList()
            notes.removeAt(selectedNotePosition)
            noteStorage.saveNotes(notes)
            adapter.updateNotes(notes)
            adapter.selectedNotePosition = -1
        }
    }

    private fun createNewNote() {
        val newNote = Note(
            id = System.currentTimeMillis(),
            title = "",
            markdownContent = ""
        )
        val notes = noteStorage.loadNotes().toMutableList()
        notes.add(newNote)
        noteStorage.saveNotes(notes)
        val bundle = Bundle().apply {
            putLong("noteId", newNote.id)
        }
        findNavController().navigate(R.id.action_noteListFragment_to_noteEditFragment, bundle)
    }

    override fun onResume() {
        super.onResume()
        val notes = noteStorage.loadNotes()
        adapter.updateNotes(notes)
    }
}