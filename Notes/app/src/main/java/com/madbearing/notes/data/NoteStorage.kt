package com.madbearing.notes.data

import android.content.Context
import com.google.gson.Gson
import com.google.gson.reflect.TypeToken
import com.madbearing.notes.models.Note
import java.io.File

class NoteStorage(private val context: Context) {

    private val gson = Gson()
    private val notesFile = File(context.filesDir, "notes.json")

    fun saveNotes(notes: List<Note>) {
        val json = gson.toJson(notes)
        notesFile.writeText(json)
    }

    fun loadNotes(): List<Note> {
        if (!notesFile.exists()) {
            return emptyList()
        }
        val json = notesFile.readText()
        val type = object : TypeToken<List<Note>>() {}.type
        return gson.fromJson(json, type)
    }
}