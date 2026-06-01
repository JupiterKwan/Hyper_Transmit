using Hyper_Transmit.Models.Enums;
using Newtonsoft.Json;
using System;

namespace Hyper_Transmit.Models
{
    /// <summary>
    /// Represents a saved SSH connection configuration.
    /// </summary>
    public class ConnectionConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "";

        public string Host { get; set; } = "";

        public int Port { get; set; } = 22;

        public string Username { get; set; } = "";

        /// <summary>
        /// Encrypted password (DPAPI). Stored as Base64 string.
        /// </summary>
        public string EncryptedPassword { get; set; } = "";

        /// <summary>
        /// Path to the private key file (PEM format).
        /// </summary>
        public string PrivateKeyPath { get; set; } = "";

        /// <summary>
        /// Encrypted private key passphrase (DPAPI). Stored as Base64 string.
        /// </summary>
        public string EncryptedPassphrase { get; set; } = "";

        public AuthenticationType AuthType { get; set; } = AuthenticationType.Password;

        public ProtocolType Protocol { get; set; } = ProtocolType.SFTP;

        public string LocalDefaultPath { get; set; } = "";

        public string RemoteDefaultPath { get; set; } = "/";

        public bool IsFavorite { get; set; }

        public DateTime? LastConnected { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// SSH host key fingerprint for host verification.
        /// </summary>
        public string? HostKeyFingerprint { get; set; }

        /// <summary>
        /// Custom display color tag for visual identification.
        /// </summary>
        public string? ColorTag { get; set; }

        /// <summary>
        /// Optional notes for this connection.
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// Display string for the connection.
        /// </summary>
        public override string ToString() => $"{Name} ({Username}@{Host}:{Port})";
    }
}