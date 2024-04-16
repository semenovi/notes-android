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

class NoteStorage(private val context: Context) {

    private val gson = GsonBuilder().registerTypeAdapter(Uri::class.java, UriSerializer()).create()
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

class UriSerializer : JsonSerializer<Uri>, JsonDeserializer<Uri> {
    override fun serialize(src: Uri, typeOfSrc: Type, context: JsonSerializationContext): JsonElement {
        return JsonPrimitive(src.toString())
    }

    override fun deserialize(json: JsonElement, typeOfT: Type, context: JsonDeserializationContext): Uri {
        return Uri.parse(json.asString)
    }
}