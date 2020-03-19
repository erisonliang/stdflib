﻿using System.Linq;
using System.Reflection;

namespace STDFLib
{
    public interface ISTDFRecordSerializer
    {
        ISTDFRecord Deserialize(ISTDFReader reader);
        //void SerializeRecord(STDFWriter writer, ISTDFRecord record);
    }

    public class STDFRecordSerializerV4 : ISTDFRecordSerializer
    {
        protected static PropertyInfo[] GetSerializeableProperties(ISTDFRecord record)
        {
            // Get the list of properties defined by the record type, then filter by
            // those that are decorated with the STDF attribute.  
            // Properties will be returned sorted based on the Order property of the STDFAttribute
            return record.GetType().GetProperties().Where(x => x.GetCustomAttributes<STDFAttribute>().Count() > 0).OrderBy(x => x.GetCustomAttribute<STDFAttribute>().Order).ToArray();
        }

        public ISTDFRecord Create(RecordType recordType)
        {

        }

        public ISTDFRecord Deserialize(ISTDFReader reader)
        {
            ISTDFRecord record = STDFFileV4.CreateRecord(reader.CurrentRecordType);

            if (record == null)
            {
                return null;
            }

            if (record is GDR)
            {
                return GenericDataRecordSerializerV4.Deserialize(reader);
            } else
            {
                // Get the list of properties that can be deserialized for the given record
                PropertyInfo[] props = GetSerializeableProperties(record);

                // If no properties to deserialize, then just return.
                if (props.Length == 0)
                {
                    return record;
                }

                // Holds the STDF attribute for each property to be deserialized
                STDFAttribute memberAttribute;

                // The number of data items to be written to the stream for array types
                int dataLength;
                int itemCount;

                foreach (var prop in props)
                {
                    memberAttribute = prop.GetCustomAttribute<STDFAttribute>();

                    dataLength = memberAttribute.DataLength;

                    // If the property is an array, then handle special
                    if (prop.PropertyType.IsArray)
                    {
                        // If a property name was specified for the ArrayCountProvider property in the data member attribute, 
                        // then get the number of array items to be written from this property
                        if (memberAttribute.ItemCountProvider != null)
                        {
                            itemCount = (int)record.GetType().GetProperty(memberAttribute.ItemCountProvider)?.GetValue(record);
                        }
                        else
                        {
                            // No item count provider, so assume that the first byte holds the length of the array
                            itemCount = reader.ReadByte();
                        }

                        prop.SetValue(record, reader.Read(prop.PropertyType.GetElementType(), itemCount, dataLength));
                    }
                    else
                    {
                        prop.SetValue(record, reader.Read(prop.PropertyType, dataLength));
                    }
                }
            }

            return record;
        }

        public ISTDFRecord DeserializeGDR(ISTDFReader reader)
        {
            GDR gdrRecord = new GDR();

            // Get the number of data fields to read in (first two bytes).  Note, we assume we have already read in the header from the stream and we are sitting on the first byte
            // of the data field count.
            int numFields = reader.ReadUInt16();

            // Save the start position of the first data record.  this will be used to make sure all record data reads start on an even by boundary
            long startPosition = reader.Position;

            // flag indicating if we are on an odd or even byte
            bool evenByte = true;

            // Holds the string representation of the field data type     
            string fieldDataType = "";

            // Holds the value read from the stream
            object fieldValue = null;

            // Read in numFields fields
            for (int i = 0; i < numFields; i++)
            {
                // Determine if we are on an even byte in the data stream
                evenByte = ((reader.Position - startPosition) % 2) == 0;

                // read the type code for the next field in the stream
                byte fieldTypeCode = (byte)reader.ReadByte();

                // if the field started on an even byte, check for a padding byte (code = 0)
                if (evenByte && fieldTypeCode == 0)
                {
                    // started on an even byte and a padding byte was found, so read the next byte for the actual field type.
                    fieldTypeCode = (byte)reader.ReadByte();

                    // actual field type was on an odd byte
                    evenByte = false;
                }

                fieldDataType = GDR.GetFieldDataType(fieldTypeCode)?.Name ?? "";

                // The type code was on an even byte.  Check to make sure the field data type does not need to
                // begin on an even byte.  If it does, then we have a problem and need to throw an exception
                if (evenByte)
                {
                    switch (fieldDataType)
                    {
                        // These types must start on an even byte alignment.  If the field type is one of these then we have an 
                        // error because we are sitting on an odd byte in the stream.
                        case "Int16":
                        case "Int32":
                        case "UInt16":
                        case "UInt32":
                        case "Single":
                        case "Double":
                            throw new STDFFormatException(string.Format("Invalid Generic Data Record format, misaligned field found.  Expected a padding byte for data type {0}, field starting at position {1}", fieldDataType, reader.Position));
                    }
                }

                // If the data type is unknown, throw an error.
                if (fieldDataType == "")
                {
                    throw new STDFFormatException(string.Format("Invalid file format or unsupported Generic Record field data type found in data stream.  Field data type code '{0}' found at position {1}", fieldTypeCode, reader.Position));
                }

                if (fieldValue != null)
                {
                    // Add the field to the generic data record
                    gdrRecord.Add(new GenericRecordDataField() { FieldType = fieldTypeCode, Value = fieldValue });
                }
            }

            // Return the generic data record
            return gdrRecord;
        }

        //public void Serialize(STDFWriter writer, ISTDFRecord record) { }

        /*

        public void Serialize(STDFWriter writer, ISTDFRecord record)
        {
            // Get the list of properties that can need to be serialized from the given record
            PropertyInfo[] props = GetSerializeableProperties(record);

            // If no properties to serialize, then just return.
            if (props.Length == 0)
            {
                return;
            }

            // Holds the STDF attribute for each property to be serialized
            STDFAttribute memberAttribute = null;

            // The number of data items to be written to the stream for array types
            int dataLength = -1;
            int itemCount = 0;
            ushort recordLength = 0;

            // We will serialize to a new STDFDataStream with a memory stream backing it.  This will build the binary representation of the record in memory so we can properly set the 
            // record length values before we commit it to the real underlying stream.  This helps in case the underlying stream in not seekable (like streaming across a network).
            using (STDFDataStream buffer = new STDFDataStream(new MemoryStream(256), writer.ByteConverter, writer.Encoding, writer.BooleanCoding, writer.DateTimeCoding, writer.StringCoding))
            {
                // write the record length (zero for now, we need to store the rest of the data first
                // to see what the actual length will be.  Then we will come back and store the real
                // value.
                buffer.WriteUInt16(0);

                // Write the rest of the header
                buffer.WriteByte(record.REC_TYP);
                buffer.WriteByte(record.REC_SUB);

                foreach (var prop in props)
                {
                    object propValue = prop.GetValue(record);

                    memberAttribute = prop.GetCustomAttribute<STDFAttribute>();

                    // If the property is an array, then handle special
                    if (propValue is Array)
                    {
                        // If a property name was specified for the ArrayCountProvider property in the data member attribute, 
                        // then get the number of array items to be written from this property
                        if (memberAttribute.ItemCountProvider != null)
                        {
                            itemCount = (int)record.GetType().GetProperty(memberAttribute.ItemCountProvider)?.GetValue(record);
                        }
                        else
                        {
                            // No special array count provider, so just use the array length as the number of items to write.
                            itemCount = ((object[])propValue).Length;
                        }

                        buffer.WriteSTDFValues((object[])propValue, itemCount, dataLength);
                    }
                    else
                    {
                        buffer.WriteSTDFValue(propValue, dataLength);
                    }
                }

                // Calculate the record length.  Record length is the buffer position pointer after serialization minus 4 bytes for the header
                // (2 bytes for record length, one byte for record type and one byte for record subtype)
                recordLength = (ushort)(buffer.Position - 4);

                // Seek back to the start of the buffer
                buffer.Seek(0, SeekOrigin.Begin);

                // Now write the record length to the buffer
                buffer.Write(recordLength);

                // Make sure all data is written to the memory stream buffer.
                buffer.Flush();

                // Seek back to the start of the buffer
                buffer.Seek(0, SeekOrigin.Begin);

                // Now write the buffer to the real data stream.  Note, the buffer will be copied starting at the current position of the To stream.
                buffer.CopyTo(writer);
            }
        }
        */
    }
}

