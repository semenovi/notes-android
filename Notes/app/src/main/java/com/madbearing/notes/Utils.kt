package com.madbearing.notes

import android.net.Uri

fun getFileNameFromUri(uri: Uri): String {
    val filePath = uri.path ?: return ""
    return filePath.substring(filePath.lastIndexOf("/") + 1)
}