package com.madbearing.notes.models

import java.util.Date

data class Note(
    val id: Long = 0,
    val title: String,
    val content: String,
    val createdAt: Date = Date(),
    val imageUris: List<String> = emptyList()
)