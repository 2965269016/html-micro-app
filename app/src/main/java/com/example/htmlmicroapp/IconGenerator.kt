package com.example.htmlmicroapp

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import kotlin.math.min

object IconGenerator {

    private const val SIZE = 108
    private val PALETTE = listOf(
        Color.parseColor("#FF6B6B"),
        Color.parseColor("#4ECDC4"),
        Color.parseColor("#45B7D1"),
        Color.parseColor("#96CEB4"),
        Color.parseColor("#FFEAA7"),
        Color.parseColor("#DDA0DD"),
        Color.parseColor("#98D8C8"),
        Color.parseColor("#F7DC6F"),
        Color.parseColor("#BB8FCE"),
        Color.parseColor("#85C1E9")
    )

    fun generate(context: Context, appName: String): Bitmap {
        val firstChar = appName.firstOrNull()?.toString() ?: "?"
        val bg = PALETTE[firstChar.hashCode() % PALETTE.size]
        val bitmap = Bitmap.createBitmap(SIZE, SIZE, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)

        // Background circle
        val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = bg
            style = Paint.Style.FILL
        }
        val radius = SIZE / 2f
        canvas.drawCircle(radius, radius, radius, paint)

        // Text
        paint.color = Color.WHITE
        paint.textAlign = Paint.Align.CENTER
        paint.textSize = SIZE * 0.45f
        val fm = paint.fontMetrics
        val y = SIZE / 2f - (fm.ascent + fm.descent) / 2
        canvas.drawText(firstChar, radius, y, paint)

        return bitmap
    }
}
