using System;

namespace Yafc.UI;

public static class MathUtils {
    public static double Clamp(double value, double min, double max) {
        if (value < min) {
            return min;
        }

        if (value > max) {
            return max;
        }

        return value;
    }

    public static float ClampF(float value, float min, float max) {
        if (value < min) {
            return min;
        }

        if (value > max) {
            return max;
        }

        return value;
    }

    public static int Clamp(int value, int min, int max) {
        if (value < min) {
            return min;
        }

        if (value > max) {
            return max;
        }

        return value;
    }

    public static int Round(double value) => (int)Math.Round(value);

    public static int Floor(double value) => (int)Math.Floor(value);

    public static int Ceil(double value) => (int)Math.Ceiling(value);

    public static byte FloatToByte(float f) {
        if (f <= 0) {
            return 0;
        }

        if (f >= 1) {
            return 255;
        }

        return (byte)MathF.Round(f * 255);
    }

    public static byte DoubleToByte(double d) {
        if (d <= 0) {
            return 0;
        }

        if (d >= 1) {
            return 255;
        }

        return (byte)Math.Round(d * 255);
    }

    public static double LogarithmicToLinear(double value, double logMin, double logMax) {
        if (value < 0d) {
            value = 0d;
        }

        double cur = Math.Log(value);
        if (cur <= logMin) {
            return 0d;
        }

        if (cur >= logMax) {
            return 1d;
        }

        return (cur - logMin) / (logMax - logMin);
    }

    public static double LinearToLogarithmic(double value, double logMin, double logMax, double min, double max) {
        if (value <= 0d) {
            return min;
        }

        if (value >= 1d) {
            return max;
        }

        double logCur = logMin + ((logMax - logMin) * value);

        return Math.Exp(logCur);
    }
}
