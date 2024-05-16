namespace ADB_Explorer.Helpers;

public class ColorHelper
{
    public static double[] BgrToHsv(params byte[] bgr)
    {
        int r = bgr[2], g = bgr[1], b = bgr[0];
        double rPrime = r / 255.0;
        double gPrime = g / 255.0;
        double bPrime = b / 255.0;

        var primes = new[] { rPrime, gPrime, bPrime };
        double max = primes.Max();
        double min = primes.Min();
        double delta = max - min;
        
        double h = 0;
        if (delta != 0)
        {
            if (max == rPrime)
            {
                h = 60 * (((gPrime - bPrime) / delta) % 6);
            }
            else if (max == gPrime)
            {
                h = 60 * (((bPrime - rPrime) / delta) + 2);
            }
            else if (max == bPrime)
            {
                h = 60 * (((rPrime - gPrime) / delta) + 4);
            }
        }

        double s = (max == 0) ? 0 : delta / max;
        double v = max;

        if (h < 0)
            h += 360;

        return new[] { h, s, v };
    }

    public static byte[] HsvToBgr(params double[] hsv)
    {
        double h = hsv[0], s = hsv[1], v = hsv[2];
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;

        double rPrime = 0;
        double gPrime = 0;
        double bPrime = 0;

        if (0 <= h && h < 60)
        {
            rPrime = c;
            gPrime = x;
            bPrime = 0;
        }
        else if (60 <= h && h < 120)
        {
            rPrime = x;
            gPrime = c;
            bPrime = 0;
        }
        else if (120 <= h && h < 180)
        {
            rPrime = 0;
            gPrime = c;
            bPrime = x;
        }
        else if (180 <= h && h < 240)
        {
            rPrime = 0;
            gPrime = x;
            bPrime = c;
        }
        else if (240 <= h && h < 300)
        {
            rPrime = x;
            gPrime = 0;
            bPrime = c;
        }
        else if (300 <= h && h < 360)
        {
            rPrime = c;
            gPrime = 0;
            bPrime = x;
        }

        byte r = (byte)((rPrime + m) * 255);
        byte g = (byte)((gPrime + m) * 255);
        byte b = (byte)((bPrime + m) * 255);

        return new[] { b, g, r };
    }
}
