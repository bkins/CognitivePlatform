package com.cognitiveplatform.wear

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Bundle
import android.speech.RecognizerIntent
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Column
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import androidx.wear.compose.material3.Button
import androidx.wear.compose.material3.Text
import androidx.wear.compose.material3.TimeText
import androidx.wear.compose.material3.Scaffold
import androidx.wear.compose.material3.CircularProgressIndicator
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class MainActivity : ComponentActivity() {
    private var status by mutableStateOf("Tap Speak to capture a thought.")
    private var isSubmitting by mutableStateOf(false)

    private val recognizer = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        val recognizedText = result.data
            ?.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS)
            ?.firstOrNull()
        if (result.resultCode != Activity.RESULT_OK || recognizedText.isNullOrBlank()) {
            status = "No speech recognized. Try again."
            return@registerForActivityResult
        }
        submit(recognizedText)
    }

    private val microphonePermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) startRecognition() else status = "Microphone permission is required to capture a thought."
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            Scaffold(timeText = { TimeText() }) {
                Column {
                    Text("CP Capture")
                    Text(status)
                    if (isSubmitting) CircularProgressIndicator() else Button(onClick = ::capture) { Text("Speak") }
                }
            }
        }
    }

    private fun capture() {
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            microphonePermission.launch(Manifest.permission.RECORD_AUDIO)
            return
        }
        startRecognition()
    }

    private fun startRecognition() {
        status = "Listening…"
        recognizer.launch(Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
            putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
            putExtra(RecognizerIntent.EXTRA_PREFER_OFFLINE, true)
            putExtra(RecognizerIntent.EXTRA_PROMPT, "Say a thought, journal entry, or task")
        })
    }

    private fun submit(text: String) {
        isSubmitting = true
        status = "Sending text to CP…"
        lifecycleScope.launch {
            val response = withContext(Dispatchers.IO) { WearConverseClient(BuildConfig.CP_API_BASE_URL).send(text) }
            isSubmitting = false
            status = if (response.success) "Captured." else "CP did not accept the capture."
        }
    }
}
