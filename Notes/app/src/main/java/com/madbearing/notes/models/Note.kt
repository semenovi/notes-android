package com.madbearing.notes.models

import android.net.Uri
import java.util.Date

data class Note(
    val id: Long = 0,
    val title: String,
    val markdownContent: String,
    val createdAt: Date = Date(),
    val imageUris: List<Uri> = emptyList()
)