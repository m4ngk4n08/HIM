using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace HIM.Gateway.Models
{
    public class RsaKeyData
    {
        public byte[]? D { get; set; }
        public byte[]? DP { get; set; }
        public byte[]? DQ { get; set; }
        public byte[]? Exponent { get; set; }
        public byte[]? InverseQ { get; set; }
        public byte[]? Modulus { get; set; }
        public byte[]? P { get; set; }
        public byte[]? Q { get; set; }

        public RSAParameters ToRSAParameters() => new RSAParameters
        {
            D = D,
            DP = DP,
            DQ = DQ,
            Exponent = Exponent,
            InverseQ = InverseQ,
            Modulus = Modulus,
            P = P,
            Q = Q
        };

        public static RsaKeyData FromRSAParameters(RSAParameters p) => new RsaKeyData
        {
            D = p.D,
            DP = p.DP,
            DQ = p.DQ,
            Exponent = p.Exponent,
            InverseQ = p.InverseQ,
            Modulus = p.Modulus,
            P = p.P,
            Q = p.Q
        };
    }
}
