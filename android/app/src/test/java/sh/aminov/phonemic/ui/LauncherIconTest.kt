package sh.aminov.phonemic.ui

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.drawable.AdaptiveIconDrawable
import android.os.Build
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.RuntimeEnvironment
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import sh.aminov.phonemic.R
import java.io.File

/**
 * Draws the launcher icon with the platform's own renderer: the whole adaptive
 * icon, the wave on its own, and the monochrome layer tinted the way Android 13+
 * themed icons tint it. The wave's strokes take their gradient from a colour
 * resource, so this runs on the oldest and newest SDKs the app is tested on.
 * The images land in app/build/launcher-icon/ for a look.
 */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class LauncherIconTest {
    private val size = 432

    @Test
    @Config(sdk = [30])
    fun drawsOnAndroid11() = draw()

    @Test
    @Config(sdk = [36])
    fun drawsOnAndroid16() = draw()

    private fun draw() {
        val context = RuntimeEnvironment.getApplication()
        val icon = context.getDrawable(R.mipmap.ic_launcher) as AdaptiveIconDrawable
        val sheet = Bitmap.createBitmap(size * 3, size, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(sheet)
        canvas.drawColor(Color.rgb(0x30, 0x30, 0x30))

        // 1. As a launcher shows it, inside the device's mask.
        icon.setBounds(0, 0, size, size)
        icon.draw(canvas)

        // 2. The wave alone, on the icon's black.
        canvas.save()
        canvas.translate(size.toFloat(), 0f)
        canvas.drawRect(0f, 0f, size.toFloat(), size.toFloat(), Paint().apply { color = Color.BLACK })
        icon.foreground.setBounds(0, 0, size, size)
        icon.foreground.draw(canvas)
        canvas.restore()

        // 3. Themed: the monochrome layer in a dark tone on a pale disc.
        if (Build.VERSION.SDK_INT >= 33) {
            canvas.save()
            canvas.translate(size * 2f, 0f)
            canvas.drawCircle(size / 2f, size / 2f, size / 2f * 0.92f, Paint().apply { color = Color.rgb(0xD8, 0xE2, 0xFF); isAntiAlias = true })
            val mono = icon.monochrome!!.mutate()
            mono.setTint(Color.rgb(0x1A, 0x2B, 0x52))
            mono.setBounds(0, 0, size, size)
            mono.draw(canvas)
            canvas.restore()
        }

        val out = File("build/launcher-icon/sdk${Build.VERSION.SDK_INT}.png").apply { parentFile!!.mkdirs() }
        out.outputStream().use { sheet.compress(Bitmap.CompressFormat.PNG, 100, it) }

        // The wave must actually be there: plenty of lit pixels in the middle of the foreground.
        var lit = 0
        for (y in size / 3 until size * 2 / 3) for (x in size + size / 4 until size + size * 3 / 4) {
            val p = sheet.getPixel(x, y)
            if (Color.red(p) + Color.green(p) + Color.blue(p) > 120) lit++
        }
        assertTrue("wave not drawn ($lit lit pixels)", lit > 500)
    }
}
