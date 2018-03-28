using MsgPack;
using MsgPack.Serialization;
using System;
using System.Collections.Generic;

namespace Audit.Fluentd.Providers
{
    internal class OrdinaryDictionarySerializer : MessagePackSerializer<IDictionary<string, object>>
    {
        private readonly SerializationContext embeddedContext;

        internal OrdinaryDictionarySerializer(SerializationContext ownerContext, SerializationContext embeddedContext) : base(ownerContext)
        {
            this.embeddedContext = embeddedContext ?? ownerContext;
        }

        protected override void PackToCore(Packer packer, IDictionary<string, object> objectTree)
        {
            packer.PackMapHeader(objectTree);
            foreach (KeyValuePair<string, object> pair in objectTree)
            {
                packer.PackString(pair.Key);
                if (pair.Value == null)
                {
                    packer.PackNull();
                }
                else
                {
                    packer.Pack(pair.Value, this.embeddedContext);
                }
            }
        }

        protected void UnpackTo(Unpacker unpacker, IDictionary<string, object> dict, long mapLength)
        {
            for (long i = 0; i < mapLength; i++)
            {
                string key;
                MessagePackObject value;
                if (!unpacker.ReadString(out key))
                {
                    throw new InvalidMessagePackStreamException("string expected for a map key");
                }
                if (!unpacker.ReadObject(out value))
                {
                    throw new InvalidMessagePackStreamException("unexpected EOF");
                }
                if (unpacker.LastReadData.IsNil)
                {
                    dict.Add(key, null);
                }
                else if (unpacker.IsMapHeader)
                {
                    long innerMapLength = value.AsInt64();
                    var innerDict = new Dictionary<string, object>();
                    UnpackTo(unpacker, innerDict, innerMapLength);
                    dict.Add(key, innerDict);
                }
                else if (unpacker.IsArrayHeader)
                {
                    long innerArrayLength = value.AsInt64();
                    var innerArray = new List<object>();
                    UnpackTo(unpacker, innerArray, innerArrayLength);
                    dict.Add(key, innerArray);
                }
                else
                {
                    dict.Add(key, value.ToObject());
                }
            }
        }

        protected void UnpackTo(Unpacker unpacker, IList<object> array, long arrayLength)
        {
            for (long i = 0; i < arrayLength; i++)
            {
                MessagePackObject value;
                if (!unpacker.ReadObject(out value))
                {
                    throw new InvalidMessagePackStreamException("unexpected EOF");
                }
                if (unpacker.IsMapHeader)
                {
                    long innerMapLength = value.AsInt64();
                    var innerDict = new Dictionary<string, object>();
                    UnpackTo(unpacker, innerDict, innerMapLength);
                    array.Add(innerDict);
                }
                else if (unpacker.IsArrayHeader)
                {
                    long innerArrayLength = value.AsInt64();
                    var innerArray = new List<object>();
                    UnpackTo(unpacker, innerArray, innerArrayLength);
                    array.Add(innerArray);
                }
                else
                {
                    array.Add(value.ToObject());
                }
            }
        }

        public new void UnpackTo(Unpacker unpacker, IDictionary<string, object> collection)
        {
            long mapLength;
            if (!unpacker.ReadMapLength(out mapLength))
            {
                throw new InvalidMessagePackStreamException("map header expected");
            }
            UnpackTo(unpacker, collection, mapLength);
        }

        protected override IDictionary<string, object> UnpackFromCore(Unpacker unpacker)
        {
            if (!unpacker.IsMapHeader)
            {
                throw new InvalidMessagePackStreamException("map header expected");
            }

            var retval = new Dictionary<string, object>();
            UnpackTo(unpacker, retval);
            return retval;
        }

        public new void UnpackTo(Unpacker unpacker, object collection)
        {
            var dictionary = collection as IDictionary<string, object>;
            if (dictionary == null)
                throw new NotSupportedException();
            UnpackTo(unpacker, dictionary);
        }
    }
}
