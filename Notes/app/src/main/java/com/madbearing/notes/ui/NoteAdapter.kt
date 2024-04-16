package com.madbearing.notes.ui

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.TextView
import androidx.core.content.ContextCompat
import androidx.navigation.findNavController
import androidx.recyclerview.widget.RecyclerView
import com.madbearing.notes.R
import com.madbearing.notes.models.Note
import io.noties.markwon.Markwon

class NoteAdapter(private var notes: List<Note>) :
    RecyclerView.Adapter<NoteAdapter.ViewHolder>() {

    var selectedNotePosition = -1

    inner class ViewHolder(itemView: View) : RecyclerView.ViewHolder(itemView) {
        val textTitle: TextView = itemView.findViewById(R.id.text_title)
        val textContent: TextView = itemView.findViewById(R.id.text_content)
        val rootView: View = itemView.findViewById(R.id.root_view)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val view = LayoutInflater.from(parent.context)
            .inflate(R.layout.item_note, parent, false)
        val rootView = view.findViewById<View>(R.id.root_view)
        rootView.setOnClickListener(null)
        return ViewHolder(view)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        val note = notes[position]
        holder.textTitle.text = note.title
        val markwon = Markwon.create(holder.itemView.context)
        markwon.setMarkdown(holder.textContent, note.markdownContent)

        val isSelected = position == selectedNotePosition
        holder.itemView.setBackgroundColor(
            if (isSelected) ContextCompat.getColor(holder.itemView.context, R.color.primary_color)
            else ContextCompat.getColor(holder.itemView.context, android.R.color.transparent)
        )

        holder.itemView.setOnClickListener {
            val bundle = Bundle().apply {
                putLong("noteId", note.id)
            }
            it.findNavController().navigate(R.id.action_noteListFragment_to_noteEditFragment, bundle)
        }

        holder.itemView.setOnLongClickListener {
            if (selectedNotePosition != position) {
                selectedNotePosition = position
                notifyDataSetChanged()
            }
            true
        }
    }

    override fun getItemCount(): Int {
        return notes.size
    }

    fun updateNotes(newNotes: List<Note>) {
        notes = newNotes
        notifyDataSetChanged()
    }
}