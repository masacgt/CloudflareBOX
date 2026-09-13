package com.cloudflarebox.app

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.shape.RoundedCornerShape

private val CloudflareBoxColors = lightColorScheme(
    primary = Color(0xFF5B3A8E),
    onPrimary = Color.White,
    primaryContainer = Color(0xFFEADCFB),
    onPrimaryContainer = Color(0xFF24113F),
    secondary = Color(0xFF006874),
    onSecondary = Color.White,
    secondaryContainer = Color(0xFFA2EEFA),
    onSecondaryContainer = Color(0xFF001F24),
    tertiary = Color(0xFF7D5260),
    background = Color(0xFFFFF7FF),
    surface = Color(0xFFFFF7FF),
    surfaceVariant = Color(0xFFEDE4EF),
    onSurface = Color(0xFF1D1A20),
    onSurfaceVariant = Color(0xFF4C454D),
    outline = Color(0xFF7D747E),
)

private val CloudflareBoxTypography = Typography().run {
    copy(
        headlineSmall = headlineSmall.copy(fontWeight = FontWeight.SemiBold),
        titleLarge = titleLarge.copy(fontWeight = FontWeight.SemiBold),
        titleMedium = titleMedium.copy(fontWeight = FontWeight.SemiBold),
        labelLarge = labelLarge.copy(fontWeight = FontWeight.SemiBold),
    )
}

private val CloudflareBoxShapes = Shapes(
    extraSmall = RoundedCornerShape(8.dp),
    small = RoundedCornerShape(12.dp),
    medium = RoundedCornerShape(18.dp),
    large = RoundedCornerShape(24.dp),
    extraLarge = RoundedCornerShape(32.dp),
)

@Composable
fun CloudflareBoxTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = CloudflareBoxColors,
        typography = CloudflareBoxTypography,
        shapes = CloudflareBoxShapes,
        content = content,
    )
}
