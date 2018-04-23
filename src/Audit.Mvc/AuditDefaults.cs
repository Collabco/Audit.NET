using System;
using System.Collections.Generic;
using System.Text;

namespace Audit.Mvc
{
    public static class AuditDefaults
    {
        /// <summary>
        /// Gets or sets a value indicating the default source of every audit event created unless otherwise specified
        /// </summary>
        public static string Source { get; set; }
    }
}
