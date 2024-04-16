package com.madbearing.notes.ui

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.TextView
import androidx.fragment.app.Fragment
import androidx.navigation.fragment.findNavController
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.google.android.material.floatingactionbutton.FloatingActionButton
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
        adapter = NoteAdapter(noteStorage.loadNotes())
        recyclerView.adapter = adapter
        view.findViewById<FloatingActionButton>(R.id.fab).setOnClickListener {
            findNavController().navigate(R.id.action_noteListFragment_to_noteEditFragment)
        }
        return view
    }

    inner class NoteAdapter(private val notes: List<Note>) :
        RecyclerView.Adapter<NoteAdapter.ViewHolder>() {

        inner class ViewHolder(itemView: View) : RecyclerView.ViewHolder(itemView)

        override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
            val view = LayoutInflater.from(parent.context)
                .inflate(R.layout.item_note, parent, false)
            return ViewHolder(view)
        }

        override fun onBindViewHolder(holder: ViewHolder, position: Int) {
            val note = notes[position]
            holder.itemView.findViewById<TextView>(R.id.text_title).text = note.title
            holder.itemView.findViewById<TextView>(R.id.text_content).text = note.content
            holder.itemView.setOnClickListener {
                val bundle = Bundle().apply {
                    putLong("noteId", note.id)
                }
                findNavController().navigate(R.id.action_noteListFragment_to_noteEditFragment, bundle)
            }
        }

        override fun getItemCount(): Int {
            return notes.size
        }
    }
}