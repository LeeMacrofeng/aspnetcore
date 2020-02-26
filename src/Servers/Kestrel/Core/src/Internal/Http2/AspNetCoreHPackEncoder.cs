// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http.HPack;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    internal class AspNetCoreHPackEncoder
    {
        private HeaderEnumerator _enumerator;

        private HPackEncoder _hPackEncoder = new HPackEncoder();

        public bool BeginEncode(HeaderEnumerator enumerator, Span<byte> buffer, out int length)
        {
            _enumerator = enumerator;
            _enumerator.MoveNext();

            return Encode1(buffer, out length);
        }

        public bool BeginEncode(int statusCode, HeaderEnumerator enumerator, Span<byte> buffer, out int length)
        {
            _enumerator = enumerator;
            _enumerator.MoveNext();

            int statusCodeLength = _hPackEncoder.EncodeStatusCode(statusCode, buffer);
            bool done = Encode1(buffer.Slice(statusCodeLength), throwIfNoneEncoded: false, out int headersLength);
            length = statusCodeLength + headersLength;

            return done;
        }

        public bool Encode1(Span<byte> buffer, out int length)
        {
            return Encode1(buffer, throwIfNoneEncoded: true, out length);
        }

        private bool Encode1(Span<byte> buffer, bool throwIfNoneEncoded, out int length)
        {
            int currentLength = 0;
            do
            {
                if (!_hPackEncoder.EncodeHeader(_enumerator.Current.Key, _enumerator.Current.Value, buffer.Slice(currentLength), out int headerLength))
                {
                    if (currentLength == 0 && throwIfNoneEncoded)
                    {
                        throw new HPackEncodingException("Failed to HPACK encode the headers.");
                    }

                    length = currentLength;
                    return false;
                }

                currentLength += headerLength;
            }
            while (_enumerator.MoveNext());

            length = currentLength;

            return true;
        }
    }

    internal struct HeaderEnumerator
    {
        private HeaderDictionary.Enumerator _headersDictionaryEnumator;
        private IEnumerator<KeyValuePair<string, StringValues>> _genericEnumerator;
        private StringValues.Enumerator _stringValuesEnumerator;

        public HeaderEnumerator(IHeaderDictionary headers)
        {
            if (headers is HeaderDictionary headerDictionary)
            {
                _headersDictionaryEnumator = headerDictionary.GetEnumerator();
                _genericEnumerator = null;
            }
            else
            {
                _headersDictionaryEnumator = default;
                _genericEnumerator = headers.GetEnumerator();
            }

            _stringValuesEnumerator = default;
            Current = default;
        }

        private string GetCurrentKey()
        {
            return (_genericEnumerator != null)
                ? _genericEnumerator.Current.Key
                : _headersDictionaryEnumator.Current.Key;
        }

        public bool MoveNext()
        {
            if (MoveNextOnStringEnumerator())
            {
                return true;
            }

            if (!TryGetNextStringEnumerator(out _stringValuesEnumerator))
            {
                return false;
            }

            return MoveNextOnStringEnumerator();
        }

        private bool MoveNextOnStringEnumerator()
        {
            var e = _stringValuesEnumerator;
            var result = e.MoveNext();
            if (result)
            {
                Current = new KeyValuePair<string, string>(GetCurrentKey(), _stringValuesEnumerator.Current);
            }
            else
            {
                Current = default;
            }
            _stringValuesEnumerator = e;
            return result;
        }

        private bool TryGetNextStringEnumerator(out StringValues.Enumerator enumerator)
        {
            if (_genericEnumerator != null)
            {
                if (!_genericEnumerator.MoveNext())
                {
                    enumerator = default;
                    return false;
                }
                else
                {
                    enumerator = _genericEnumerator.Current.Value.GetEnumerator();
                    return true;
                }
            }
            else
            {
                if (!_headersDictionaryEnumator.MoveNext())
                {
                    enumerator = default;
                    return false;
                }
                else
                {
                    enumerator = _headersDictionaryEnumator.Current.Value.GetEnumerator();
                    return true;
                }
            }
        }

        public KeyValuePair<string, string> Current { get; private set; }
    }
}
