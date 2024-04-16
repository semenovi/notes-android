package com.madbearing.notes.ui

import android.net.Uri
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import com.madbearing.notes.R

class ImageAdapter(
    private val imageUris: List<Uri>,
    private val onImageClicked: (Uri) -> Unit
) : RecyclerView.Adapter<ImageAdapter.ViewHolder>() {

    inner class ViewHolder(itemView: View) : RecyclerView.ViewHolder(itemView) {
        val imageView: ImageView = itemView.findViewById(R.id.image_view)
        val textView: TextView = itemView.findViewById(R.id.text_view_filename)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val view = LayoutInflater.from(parent.context)
            .inflate(R.layout.item_image, parent, false)
        return ViewHolder(view)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        val imageUri = imageUris[position]
        holder.imageView.setImageURI(imageUri)
        holder.textView.text = getFileNameFromUri(imageUri)
        holder.itemView.setOnClickListener {
            onImageClicked(imageUri)
        }
    }

    override fun getItemCount(): Int = imageUris.size

    private fun getFileNameFromUri(uri: Uri): String {
        val filePath = uri.path ?: return ""
        return filePath.substring(filePath.lastIndexOf("/") + 1)
    }
}