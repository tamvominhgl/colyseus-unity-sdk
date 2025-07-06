using System;
using System.Buffers;
using System.Collections.Generic;

namespace Colyseus.Schema
{
    public delegate void TriggerChangesDelegate(ref List<DataChange> changes);

    public class Decoder<T> where T : Schema
    {
        public ColyseusReferenceTracker Refs = new ColyseusReferenceTracker();
        public TypeContext Context = new TypeContext();
        public T State;

        public TriggerChangesDelegate TriggerChanges;
		protected List<DataChange> AllChanges = new List<DataChange>();

		public Decoder()
		{
            State = Activator.CreateInstance<T>();
            Refs.Add(0, State);
        }

		/// <summary>
		///     Decode incoming data
		/// </summary>
		/// <param name="bytes">The incoming data</param>
		/// <param name="it"><see cref="Iterator" /> used to  If null, will create a new one</param>
		/// <param name="refs">
		///     <see cref="ColyseusReferenceTracker" /> for all refs found through the decoding process. If null, will
		///     create a new one
		/// </param>
		/// <exception cref="Exception">If no decoding fails</exception>
		public void Decode(ref SequenceReader<byte> reader)
		{
			int refId = 0;
			IRef _ref = State;

			AllChanges.Clear();

			while (reader.Remaining > 0)
			{
				if (reader.IsNext((byte)SPEC.SWITCH_TO_STRUCTURE, true))
				{
					refId = Convert.ToInt32(Utils.Decode.DecodeNumber(ref reader));

					if (_ref is IArraySchema arraySchema) { arraySchema.OnDecodeEnd(); }

					_ref = Refs.Get(refId);

					//
					// Trying to access a reference that haven't been decoded yet.
					//
					if (_ref == null)
					{
						throw new Exception("refId not found: " + refId);
					}

					continue;
				}

				bool isSchemaDefinitionMismatch;

				if (_ref is Schema)
				{
					isSchemaDefinitionMismatch = !DecodeSchema(ref reader, (Schema)_ref);
				}
				else if (_ref is IMapSchema)
				{
					isSchemaDefinitionMismatch = !DecodeMapSchema(ref reader, (IMapSchema)_ref);
				}
				else
				{
					isSchemaDefinitionMismatch = !DecodeArraySchema(ref reader, (IArraySchema)_ref);
				}

				if (isSchemaDefinitionMismatch)
				{
					//
					// keep skipping next bytes until reaches a known structure
					// by local decoder.
					//

					while (reader.Remaining > 0)
					{
						reader.TryRead(out var spec);
						if (spec == (byte)SPEC.SWITCH_TO_STRUCTURE)
						{
							var consumed = reader.Consumed;
							if (Refs.Has(Convert.ToInt32(Utils.Decode.DecodeNumber(ref reader))))
							{
								break;
							}
							else
							{
								reader.Rewind(reader.Consumed - consumed);
							}
						}
					}

					continue;
				}
			}

			if (_ref is IArraySchema) { ((IArraySchema)_ref).OnDecodeEnd(); }

			TriggerChanges?.Invoke(ref AllChanges);

			Refs.GarbageCollection();
		}

		protected void DecodeValue(ref SequenceReader<byte> reader, IRef _ref, int fieldIndex, string fieldType, System.Type childType, byte operation, out object value, out object previousValue)
		{
			previousValue = _ref.GetByIndex(fieldIndex);

			//
			// Delete operations
			//
			if ((operation & (byte)OPERATION.DELETE) == (byte)OPERATION.DELETE)
			{
				// Flag `refId` for garbage collection.
				if (previousValue != null && previousValue is IRef)
				{
					Refs.Remove(((IRef)previousValue).__refId);
				}

				if (operation != (byte)OPERATION.DELETE_AND_ADD)
				{
					_ref.DeleteByIndex(fieldIndex);
				}

				value = null;
			}

			if (operation == (byte)OPERATION.DELETE)
			{
				//
				// FIXME: refactor me.
				// Don't do anything.
				//
				value = null;

			}
			else if (fieldType == "ref")
			{
				var __refId = Convert.ToInt32(Utils.Decode.DecodeNumber(ref reader));
				value = Refs.Get(__refId);

				if ((operation & (byte)OPERATION.ADD) == (byte)OPERATION.ADD)
				{
					System.Type concreteChildType = GetSchemaType(ref reader, childType);
					if (value == null)
					{
						value = CreateTypeInstance(concreteChildType);
						((IRef)value).__refId = __refId;
					}
					Refs.Add(__refId, (IRef)value, (
						value != previousValue ||  // increment ref count if value has changed
						(operation == (byte)OPERATION.DELETE_AND_ADD && value == previousValue) // increment ref count if the same instance is being added again
					));
				}

			}
			else if (childType == null)
			{
				// primitive values
				value = Utils.Decode.DecodePrimitiveType(fieldType, ref reader);
			}
			else
			{
				var __refId = Convert.ToInt32(Utils.Decode.DecodeNumber(ref reader));

				ISchemaCollection valueRef = Refs.Has(__refId)
					? (ISchemaCollection)previousValue ?? (ISchemaCollection)Refs.Get(__refId)
					: (ISchemaCollection)Activator.CreateInstance(childType);

				value = valueRef.Clone();
				((ISchemaCollection)value).__refId = __refId;

				// keep reference to nested childPrimitiveType.
				string childPrimitiveType;
				((Schema)_ref).fieldsByIndex.TryGetValue(fieldIndex, out var fieldName);
				((Schema)_ref).fieldChildPrimitiveTypes.TryGetValue(fieldName, out childPrimitiveType);
				((ISchemaCollection)value).ChildPrimitiveType = childPrimitiveType;

				if (previousValue != null)
				{
					if (
						((IRef)previousValue).__refId > 0 &&
						__refId != ((IRef)previousValue).__refId
					)
					{
						((ISchemaCollection)previousValue).ForEach((key, value) => {
							if (value is IRef) {
								Refs.Remove(((IRef)value).__refId);
							}

							AllChanges.Add(new DataChange
							{
								RefId = __refId,
								DynamicIndex = key,
								Op = (byte)OPERATION.DELETE,
								Value = null,
								PreviousValue = value
							});
						});
					}
				}

				Refs.Add(__refId, (IRef)value, (
					valueRef != previousValue || // increment ref count if value has changed
					(operation == (byte)OPERATION.DELETE_AND_ADD && valueRef == previousValue) // increment ref count if the same instance is being added again
				));
			}
		}

		protected bool DecodeSchema(ref SequenceReader<byte> reader, Schema refSchema)
		{
			reader.TryRead(out byte firstByte);
			byte operation = (byte) ((firstByte >> 6) << 6); // "compressed" index + operation

			int fieldIndex = firstByte % (operation == 0 ? 255 : operation); // FIXME: JS allows (0 || 255);

			refSchema.fieldsByIndex.TryGetValue(fieldIndex, out var fieldName);
			refSchema.fieldTypes.TryGetValue(fieldName ?? "", out var fieldType);
			refSchema.fieldChildTypes.TryGetValue(fieldName ?? "", out var childType);

			if (fieldName == null)
			{
				return false;
			}

			DecodeValue(
				ref reader,
				refSchema,
				fieldIndex,
				fieldType,
				childType,
				operation,
				out var value,
				out var previousValue
			);

			if (value != null)
			{
				refSchema[fieldName] = value;
			}

			if (previousValue != value)
			{
				AllChanges.Add(new DataChange
				{
					RefId = refSchema.__refId,
					Op = operation,
					Field = fieldName,
					Value = value,
					PreviousValue = previousValue
				});
			}

			return true;
		}

		protected bool DecodeMapSchema (ref SequenceReader<byte> reader, IMapSchema refMap)
		{
			reader.TryRead(out byte operation);

			if (operation == (byte)OPERATION.CLEAR)
			{
				refMap.Clear(AllChanges, Refs);
				return true;
			}

			int fieldIndex = Convert.ToInt32(Utils.Decode.DecodeNumber(ref reader));
			string fieldType;
			System.Type childType = null;

			if (refMap.HasSchemaChild)
			{
				fieldType = "ref";
				childType = refMap.GetChildType();
			}
			else
			{
				fieldType = refMap.ChildPrimitiveType;
			}

			string dynamicIndex;

			if ((operation & (byte)OPERATION.ADD) == (byte)OPERATION.ADD)
			{
				// MapSchema dynamic index.
				dynamicIndex = Utils.Decode.DecodeString(ref reader);
				refMap.SetIndex(fieldIndex, dynamicIndex);
			}
			else
			{
				dynamicIndex = (string)refMap.GetIndex(fieldIndex);
			}

			DecodeValue(
				ref reader,
				refMap,
				fieldIndex,
				fieldType,
				childType,
				operation,
				out var value,
				out var previousValue
			);

			if (value != null)
			{
				refMap.SetByIndex(fieldIndex, dynamicIndex, value);
			}

			if (previousValue != value)
			{
				AllChanges.Add(new DataChange
				{
					RefId = refMap.__refId,
					Op = operation,
					Field = null,
					DynamicIndex = dynamicIndex,
					Value = value,
					PreviousValue = previousValue
				});
			}
			return true;
		}

		protected bool DecodeArraySchema(ref SequenceReader<byte> reader, IArraySchema refArray)
		{
			reader.TryRead(out byte operation);
			int index;

			if (operation == (byte)OPERATION.CLEAR)
			{
				refArray.Clear(AllChanges, Refs);
				return true;

			}
			else if (operation == (byte)OPERATION.REVERSE)
			{
				refArray.Reverse();
				return true;

			}
			else if (operation == (byte)OPERATION.DELETE_BY_REFID)
			{
				// TODO: refactor here, try to follow same flow as below
				int refId = Convert.ToInt32(Utils.Decode.DecodeNumber(ref reader));
				object itemByRefId = Refs.Get(refId);
				int i = 0;
				index = -1;
				foreach (var item in refArray.GetItems())
				{
					if (item == itemByRefId)
					{
						index = i;
						break;
					}
					i++;
				}
				refArray.DeleteByIndex(index);
				AllChanges.Add(new DataChange
				{
					RefId = refArray.__refId,
					Op = (byte) OPERATION.DELETE,
					Field = "",
					DynamicIndex = index,
					Value = null,
					PreviousValue = itemByRefId
				});
				return true;

			}
			else if (operation == (byte)OPERATION.ADD_BY_REFID)
			{
				int refId = Convert.ToInt32(Utils.Decode.DecodeNumber(ref reader));
				IRef itemByRefId = Refs.Get(refId);
				if (itemByRefId != null)
				{
					int i = 0;
					index = -1;
					foreach (var item in refArray.GetItems())
					{
						if (item == itemByRefId)
						{
							index = i;
							break;
						}
						i++;
					}
				}
				else
				{
					index = refArray.Count;
				}

			}
			else
			{
				index = Convert.ToInt32(Utils.Decode.DecodeNumber(ref reader));
			}

			string fieldType;
			System.Type childType = null;

			if (refArray.HasSchemaChild)
			{
				fieldType = "ref";
				childType = refArray.GetChildType();
			}
			else
			{
				fieldType = refArray.ChildPrimitiveType;
			}

			DecodeValue(
				ref reader,
				refArray,
				index,
				fieldType,
				childType,
				operation,
				out var value,
				out var previousValue
			);

			if (value != null)
			{
				refArray.SetByIndex(index, value, operation);
			}

			if (previousValue != value)
			{
				AllChanges.Add(new DataChange
				{
					RefId = refArray.__refId,
					Op = operation,
					Field = null,
					DynamicIndex = index,
					Value = value,
					PreviousValue = previousValue
				});
			}
			return true;
		}

		/// <summary>
		///     Determine what type of <see cref="Schema" /> this is
		/// </summary>
		/// <param name="bytes">Incoming data</param>
		/// <param name="it">
		///     The <see cref="Iterator" /> used to <see cref="Decoder.DecodeNumber" /> the <paramref name="bytes" />
		/// </param>
		/// <param name="defaultType">
		///     The default <see cref="Schema" /> type, if one cant be determined from the
		///     <paramref name="bytes" />
		/// </param>
		/// <returns>The parsed <see cref="System.Type" /> if found, <paramref name="defaultType" /> if not</returns>
		protected System.Type GetSchemaType(ref SequenceReader<byte> reader, System.Type defaultType)
        {
            System.Type type = defaultType;

            if (reader.Remaining > 0 && reader.IsNext((byte)SPEC.TYPE_ID, true))
            {
                int typeId = Convert.ToInt32(Utils.Decode.DecodeNumber(ref reader));
                type = Context.Get(typeId);
            }

            return type;
        }

        /// <summary>
        ///     Create an instance of the provided <paramref name="type" />
        /// </summary>
        /// <param name="type">The <see cref="System.Type" /> to create an instance of</param>
        /// <returns></returns>
        protected object CreateTypeInstance(System.Type type)
        {
            return Activator.CreateInstance(type);
        }

        internal void Teardown()
		{
            Refs.Clear();
        }

	}
}