﻿using System.Collections;
using System.Linq;
using System.Text;

namespace STDFLib
{
    public abstract class STDFRecord : ISTDFRecord
    {
        public virtual RecordType TypeCode => 0x0000;

        // This base class is for records conforming to Version 4.0 of the STDF specification.
        public STDFVersions Version { get; } = STDFVersions.STDFVer4;

        public abstract string Description { get; }

        public STDFRecord()
        {
        }

        // Pretty (maybe) formatting of the record contents for debugging
        public override string ToString()
        {
            var props = GetType().GetProperties().Where(x => x.GetCustomAttributes(typeof(STDFAttribute), false).Count() > 0);

            StringBuilder sb = new StringBuilder();

            sb.Append("--------------------------------------------------------\n");
            sb.AppendFormat("{0,25}:{1,-30}\n", "RECORD", this.GetType().Name);
            sb.Append("========================= ==============================\n");

            foreach (var prop in props)
            {
                object propValue = prop.GetValue(this);

                if (propValue == null)
                {
                    sb.AppendFormat("{0,25}:{1,-30}\n", prop.Name, "Null");
                    continue;
                }

                if (prop.PropertyType.Name.EndsWith("[]") || prop.PropertyType.Name.StartsWith("List`1") && propValue != null)
                {
                    int count = 0;
                    sb.AppendFormat("{0,25}:LIST/ARRAY OF {1} ", 
                        prop.Name, 
                        propValue.GetType().Name.EndsWith("[]") ? propValue.GetType().GetElementType().Name : propValue.GetType().GetGenericArguments()[0].Name);

                    IEnumerator pv = ((IEnumerable)propValue)?.GetEnumerator();
                    
                    pv.Reset();

                    while (pv.MoveNext())
                    {
                        if(count == 0)
                        {
                            sb.Append("\n");
                        }
                        sb.AppendFormat("{0,-55}\n", pv.Current.ToString());
                        count++;
                    }

                    if (count == 0)
                    {
                        sb.Append("(*EMPTY)\n");
                    }
                }
                else
                {
                    sb.AppendFormat("{0,25}:{1,-30}\n", prop.Name, propValue?.ToString());
                }
            }

            sb.AppendLine();

            return sb.ToString();
        }
    }
}

