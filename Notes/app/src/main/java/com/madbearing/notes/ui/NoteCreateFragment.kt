package com.madbearing.notes.ui

import android.app.Activity
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.webkit.MimeTypeMap
import android.widget.Button
import android.widget.EditText
import androidx.fragment.app.Fragment
import androidx.navigation.fragment.findNavController
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.madbearing.notes.R
import com.madbearing.notes.data.NoteStorage
import com.madbearing.notes.models.Note
import java.io.File

class NoteCreateFragment : Fragment() {

    private lateinit var noteStorage: NoteStorage
    private lateinit var editTitle: EditText
    private lateinit var editContent: EditText

    private val imageUris = mutableListOf<Uri>()
    private lateinit var imageAdapter: ImageAdapter

    private val REQUEST_CODE_PICK_IMAGES = 1

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?
    ): View? {
        val view = inflater.inflate(R.layout.fragment_note_create, container, false)
        noteStorage = NoteStorage(requireContext())
        editTitle = view.findViewById(R.id.edit_title)
        editContent = view.findViewById(R.id.edit_content)

        val recyclerViewImages = view.findViewById<RecyclerView>(R.id.recycler_view_images)
        imageAdapter = ImageAdapter(imageUris, ::insertImageMarkdownLink)
        recyclerViewImages.adapter = imageAdapter
        recyclerViewImages.layoutManager = LinearLayoutManager(context, LinearLayoutManager.HORIZONTAL, false)

        view.findViewById<Button>(R.id.button_save).setOnClickListener {
            saveNote()
        }

        view.findViewById<Button>(R.id.button_add_image).setOnClickListener {
            openImagePicker()
        }

        return view
    }

    private fun saveNote() {
        val noteTitle = editTitle.text.toString().trim()
        val markdownContent = editContent.text.toString().trim()

        if (noteTitle.isNotEmpty() && markdownContent.isNotEmpty()) {
            val newNote = Note(
                id = System.currentTimeMillis(),
                title = noteTitle,
                markdownContent = markdownContent,
                imageUris = imageUris.map { it.toAbsolutePath() }
            )
            val notes = noteStorage.loadNotes().toMutableList()
            notes.add(newNote)
            noteStorage.saveNotes(notes)
            findNavController().navigateUp()
        } else {
            showEmptyFieldsError()
        }
    }

    private fun showEmptyFieldsError() {
        // TODO: Implement error handling
    }

    private fun openImagePicker() {
        val intent = Intent(Intent.ACTION_GET_CONTENT)
        intent.type = "image/*"
        intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true)
        startActivityForResult(Intent.createChooser(intent, "Select Images"), REQUEST_CODE_PICK_IMAGES)
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == REQUEST_CODE_PICK_IMAGES && resultCode == Activity.RESULT_OK) {
            data?.clipData?.let { clipData ->
                for (i in 0 until clipData.itemCount) {
                    val imageUri = clipData.getItemAt(i).uri
                    saveImageToAppDirectory(imageUri)
                    imageUris.add(imageUri)
                }
            } ?: run {
                data?.data?.let { uri ->
                    saveImageToAppDirectory(uri)
                    imageUris.add(uri)
                }
            }
            updateImageList()
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
        imageUris.add(Uri.fromFile(outputFile))
    }

    private fun getExtensionFromUri(uri: Uri): String {
        val fileExtension = MimeTypeMap.getSingleton()
            .getExtensionFromMimeType(context?.contentResolver?.getType(uri))
        return fileExtension ?: "jpg"
    }

    private fun updateImageList() {
        imageAdapter.notifyDataSetChanged()
    }

    private fun insertImageMarkdownLink(imageUri: Uri) {
        val fileName = getFileNameFromUri(imageUri)
        val markdownLink = "![Image]($fileName)"
        editContent.append("\n$markdownLink")
    }

    private fun getFileNameFromUri(uri: Uri): String {
        val filePath = uri.path ?: return ""
        return filePath.substring(filePath.lastIndexOf("/") + 1)
    }

    private fun Uri.toAbsolutePath(): String {
        val filePath = this.path ?: return ""
        return if (filePath.startsWith("/")) filePath else "/files/${context?.filesDir?.absolutePath}/${filePath}"
    }
}