package com.cognitiveplatform.wear

import java.net.HttpURLConnection
import java.net.URL
import java.util.UUID

data class WearConverseResponse(val success: Boolean, val message: String)

class WearConverseClient(private val apiBaseUrl: String) {
    fun send(text: String): WearConverseResponse {
        val connection = (URL(apiBaseUrl + "api/conversation/converse").openConnection() as HttpURLConnection)
        connection.requestMethod = "POST"
        connection.setRequestProperty("Content-Type", "application/json")
        connection.connectTimeout = 10_000
        connection.readTimeout = 20_000
        connection.doOutput = true

        val payload = """{"sessionId":"wear-${UUID.randomUUID()}","input":${text.toJsonString()},"fastPath":false,"streaming":false}"""
        connection.outputStream.bufferedWriter().use { writer -> writer.write(payload) }

        val responseBody = (if (connection.responseCode in 200..299) connection.inputStream else connection.errorStream)
            ?.bufferedReader()
            ?.use { reader -> reader.readText() }
            .orEmpty()
        return WearConverseResponse(connection.responseCode in 200..299, responseBody)
    }
}

private fun String.toJsonString(): String = buildString {
    append('"')
    this@toJsonString.forEach { character ->
        append(
            when (character) {
                '\\' -> "\\\\"
                '"' -> "\\\""
                '\n' -> "\\n"
                '\r' -> "\\r"
                '\t' -> "\\t"
                else -> character
            }
        )
    }
    append('"')
}
