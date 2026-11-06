package com.example.htmlmicroapp

import android.content.Context
import android.graphics.*
import android.util.Log
import org.json.JSONObject
import java.io.File
import java.security.MessageDigest

class LocalStore(private val context: Context) {

    private val prefs = context.getSharedPreferences(PREF_NAME, Context.MODE_PRIVATE)

    @android.webkit.JavascriptInterface
    fun save(key: String, value: String): String {
        return try {
            prefs.edit().putString(key, value).apply()
            """{"ok":true}"""
        } catch (e: Exception) {
            """{"ok":false,"error":"${e.message}"}"""
        }
    }

    @android.webkit.JavascriptInterface
    fun load(key: String): String {
        return try {
            val v = prefs.getString(key, null)
            JSONObject().apply { put("ok", true); put("value", v ?: JSONObject.NULL) }.toString()
        } catch (e: Exception) {
            """{"ok":false,"error":"${e.message}"}"""
        }
    }

    @android.webkit.JavascriptInterface
    fun remove(key: String): String {
        return try {
            prefs.edit().remove(key).apply()
            """{"ok":true}"""
        } catch (e: Exception) {
            """{"ok":false,"error":"${e.message}"}"""
        }
    }

    @android.webkit.JavascriptInterface
    fun clear(): String {
        return try {
            prefs.edit().clear().apply()
            """{"ok":true}"""
        } catch (e: Exception) {
            """{"ok":false,"error":"${e.message}"}"""
        }
    }

    companion object {
        private const val PREF_NAME = "html_micro_app_store"
        private const val TAG = "LocalStore"
    }
}
