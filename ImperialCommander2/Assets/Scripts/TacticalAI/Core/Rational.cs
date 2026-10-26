// Exact rational arithmetic mirroring Python's fractions.Fraction.
// ToString uses the Python str(Fraction) form: "n" for integral, "n/d".
using System;
using System.Globalization;
using System.Numerics;

namespace TacticalAI.Core
{
    public readonly struct Rational : IEquatable<Rational>, IComparable<Rational>
    {
        public readonly BigInteger Num;   // normalized: Den > 0, gcd(|Num|,Den)=1
        public readonly BigInteger Den;

        public Rational(BigInteger num, BigInteger den = default)
        {
            if (den.IsZero) den = 1;
            if (den < 0) { num = -num; den = -den; }
            var g = BigInteger.GreatestCommonDivisor(num < 0 ? -num : num, den);
            if (g == 0) g = 1;
            Num = num / g;
            Den = den / g;
        }

        public static Rational Zero => new Rational(0);
        public static Rational One => new Rational(1);
        public static implicit operator Rational(int v) => new Rational(v);

        public static Rational Parse(string s)
        {
            int slash = s.IndexOf('/');
            if (slash < 0) return new Rational(BigInteger.Parse(s, CultureInfo.InvariantCulture));
            return new Rational(BigInteger.Parse(s.Substring(0, slash), CultureInfo.InvariantCulture),
                                BigInteger.Parse(s.Substring(slash + 1), CultureInfo.InvariantCulture));
        }

        public static Rational operator +(Rational a, Rational b) => new Rational(a.Num * b.Den + b.Num * a.Den, a.Den * b.Den);
        public static Rational operator -(Rational a, Rational b) => new Rational(a.Num * b.Den - b.Num * a.Den, a.Den * b.Den);
        public static Rational operator -(Rational a) => new Rational(-a.Num, a.Den);
        public static Rational operator *(Rational a, Rational b) => new Rational(a.Num * b.Num, a.Den * b.Den);
        public static Rational operator /(Rational a, Rational b) => new Rational(a.Num * b.Den, a.Den * b.Num);
        public static Rational operator /(Rational a, int b) => new Rational(a.Num, a.Den * b);

        public static bool operator <(Rational a, Rational b) => a.Num * b.Den < b.Num * a.Den;
        public static bool operator >(Rational a, Rational b) => a.Num * b.Den > b.Num * a.Den;
        public static bool operator <=(Rational a, Rational b) => a.Num * b.Den <= b.Num * a.Den;
        public static bool operator >=(Rational a, Rational b) => a.Num * b.Den >= b.Num * a.Den;
        public static bool operator ==(Rational a, Rational b) => a.Num == b.Num && a.Den == b.Den;
        public static bool operator !=(Rational a, Rational b) => !(a == b);

        public bool Equals(Rational other) => this == other;
        public override bool Equals(object obj) => obj is Rational r && this == r;
        public override int GetHashCode() => Num.GetHashCode() * 31 + Den.GetHashCode();
        public int CompareTo(Rational other) => this < other ? -1 : this > other ? 1 : 0;

        public double ToDouble() => (double)Num / (double)Den;
        public bool IsZero => Num.IsZero;

        // Python str(Fraction)
        public override string ToString() => Den == 1 ? Num.ToString() : Num + "/" + Den;

        public static Rational Min(Rational a, Rational b) => a <= b ? a : b;
        public static Rational Max(Rational a, Rational b) => a >= b ? a : b;
    }
}
