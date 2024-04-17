package com.madbearing.notes.ui

import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Bundle
import android.provider.MediaStore
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.webkit.MimeTypeMap
import android.widget.Button
import android.widget.EditText
import androidx.core.content.ContextCompat
import androidx.fragment.app.Fragment
import androidx.navigation.fragment.findNavController
import com.madbearing.notes.R
import com.madbearing.notes.data.NoteStorage
import com.madbearing.notes.models.Note
import java.io.File

class NoteEditFragment : Fragment() {

    private lateinit var noteStorage: NoteStorage
    private lateinit var editTitle: EditText
    private lateinit var editContent: EditText
    private lateinit var buttonAddImage: Button
    private var noteId: Long = 0

    private val CHOOSE_IMAGE_REQUEST = 1
    private val PERMISSION_REQUEST_CODE = 2

    private val imageUris = mutableListOf<Uri>()

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?
    ): View? {
        val view = inflater.inflate(R.layout.fragment_note_edit, container, false)
        noteStorage = NoteStorage(requireContext())
        editTitle = view.findViewById(R.id.edit_title)
        editContent = view.findViewById(R.id.edit_content)
        buttonAddImage = view.findViewById(R.id.button_add_image)
        buttonAddImage.setOnClickListener {
            checkPermission()
        }
        view.findViewById<Button>(R.id.button_save).setOnClickListener {
            saveNote()
        }

        arguments?.let {
            noteId = it.getLong("noteId")
            val note = noteStorage.loadNotes().find { it.id == noteId }
            note?.let {
                displayNote(it)
            }
        }

        return view
    }

    private fun checkPermission() {
        if (ContextCompat.checkSelfPermission(
                requireContext(),
                android.Manifest.permission.READ_EXTERNAL_STORAGE
            ) != PackageManager.PERMISSION_GRANTED
        ) {
            requestPermissions(arrayOf(android.Manifest.permission.READ_EXTERNAL_STORAGE), PERMISSION_REQUEST_CODE)
        } else {
            openImagePicker()
        }
    }

    private fun openImagePicker() {
        val intent = Intent(Intent.ACTION_PICK, MediaStore.Images.Media.EXTERNAL_CONTENT_URI)
        startActivityForResult(intent, CHOOSE_IMAGE_REQUEST)
    }

    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray
    ) {
        if (requestCode == PERMISSION_REQUEST_CODE) {
            if (grantResults.isNotEmpty() && grantResults[0] == PackageManager.PERMISSION_GRANTED) {
                openImagePicker()
            } else {
                // Разрешение не предоставлено, обработайте эту ситуацию соответствующим образом
            }
        }
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == CHOOSE_IMAGE_REQUEST && resultCode == Activity.RESULT_OK) {
            val imageUri = data?.data
            imageUri?.let {
                saveImageToAppDirectory(it)
            }
        }
    }

    private var imageCounter = 0

    private fun saveImageToAppDirectory(imageUri: Uri) {
        val inputStream = context?.contentResolver?.openInputStream(imageUri)
        val extension = getExtensionFromUri(imageUri)
        val fileName = "${imageCounter++}.$extension"
        val outputFile = File(context?.filesDir, fileName)
        inputStream?.use { input ->
            outputFile.outputStream().use { output ->
                input.copyTo(output)
            }
        }
        val appImageUri = Uri.fromFile(outputFile)
        imageUris.add(appImageUri)
    }

    private fun getExtensionFromUri(uri: Uri): String {
        val fileExtension = context?.contentResolver?.getType(uri)?.let {
            MimeTypeMap.getSingleton().getExtensionFromMimeType(it)
        }
        return fileExtension ?: "jpg"
    }

    private fun displayNote(note: Note) {
        editTitle.setText(note.title)
        editContent.setText(note.markdownContent)
        imageUris.clear()
        imageUris.addAll(note.imageUris)
    }

    private fun saveNote() {
        val noteTitle = editTitle.text.toString().trim()
        val markdownContent = editContent.text.toString().trim()

        if (noteTitle.isNotEmpty() && markdownContent.isNotEmpty()) {
            val updatedNote = Note(
                id = noteId,
                title = noteTitle,
                markdownContent = markdownContent,
                imageUris = imageUris
            )
            val notes = noteStorage.loadNotes().toMutableList()
            val index = notes.indexOfFirst { it.id == noteId }
            if (index != -1) {
                notes[index] = updatedNote
                noteStorage.saveNotes(notes)
                findNavController().navigateUp()
            }
        } else {
            showEmptyFieldsError()
        }
    }

    private fun showEmptyFieldsError() {
        TODO("Not yet implemented")
    }
}