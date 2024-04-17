package com.madbearing.notes.data

import android.content.Context
import android.net.Uri
import com.google.gson.GsonBuilder
import com.google.gson.JsonDeserializationContext
import com.google.gson.JsonDeserializer
import com.google.gson.JsonElement
import com.google.gson.JsonPrimitive
import com.google.gson.JsonSerializationContext
import com.google.gson.JsonSerializer
import com.google.gson.reflect.TypeToken
import com.madbearing.notes.models.Note
import java.io.File
import java.lang.reflect.Type
import com.madbearing.notes.getFileNameFromUri

class NoteStorage(private val context: Context) {

    private val gson = GsonBuilder().registerTypeAdapter(Uri::class.java, UriSerializer()).create()
    private val notesFile = File(context.filesDir, "notes.json")

    fun saveNotes(notes: List<Note>) {
        val json = gson.toJson(notes)
        notesFile.writeText(json)
        saveImages(notes)
    }

    fun loadNotes(): List<Note> {
        if (!notesFile.exists()) {
            return emptyList()
        }
        val json = notesFile.readText()
        val type = object : TypeToken<List<Note>>() {}.type
        val notes = gson.fromJson<List<Note>>(json, type)
        loadImages(notes)
        return notes
    }

    private fun saveImages(notes: List<Note>) {
        val imagesDir = File(context.filesDir, "images")
        if (!imagesDir.exists()) {
            imagesDir.mkdir()
        }
        notes.forEach { note ->
            note.imageUris.forEach { uri ->
                val fileName = getFileNameFromUri(uri)
                val sourceFile = File(uri.path)
                if (sourceFile.exists()) {
                    val destinationFile = File(imagesDir, fileName)
                    sourceFile.copyTo(destinationFile, overwrite = true)
                }
            }
        }
    }

    private fun loadImages(notes: List<Note>) {
        val imagesDir = File(context.filesDir, "images")
        notes.forEach { note ->
            val updatedImageUris = note.imageUris.map { uri ->
                val fileName = getFileNameFromUri(uri)
                val imageFile = File(imagesDir, fileName)
                Uri.fromFile(imageFile)
            }
            note.imageUris = updatedImageUris
        }
    }
}

class UriSerializer : JsonSerializer<Uri>, JsonDeserializer<Uri> {
    override fun serialize(src: Uri, typeOfSrc: Type, context: JsonSerializationContext): JsonElement {
        return JsonPrimitive(src.toString())
    }

    override fun deserialize(json: JsonElement, typeOfT: Type, context: JsonDeserializationContext): Uri {
        return Uri.parse(json.asString)
    }
}