// Copyright (C) 2017 ichenq@outlook.com. All rights reserved.
// Distributed under the terms and conditions of the MIT License.
// See accompanying files LICENSE.

using System;

namespace Network
{
    public class Utils
    {
        private static readonly DateTime epoch = new DateTime(1970, 1, 1);
        private static readonly DateTime twepoch = new DateTime(2000, 1, 1);

        public static UInt32 iclock()
        {
            var now = Convert.ToInt64(DateTime.Now.Subtract(twepoch).TotalMilliseconds);
            return (UInt32)(now & 0xFFFFFFFF);
        }

        public static Int64 LocalUnixTime()
        {
            return Convert.ToInt64(DateTime.Now.Subtract(epoch).TotalMilliseconds);
        }

        // local datetime to unix timestamp
        public static Int64 ToUnixTimestamp(DateTime t)
        {
            var timespan = t.ToUniversalTime().Subtract(epoch);
            return (Int64)Math.Truncate(timespan.TotalSeconds);
        }
    }
}