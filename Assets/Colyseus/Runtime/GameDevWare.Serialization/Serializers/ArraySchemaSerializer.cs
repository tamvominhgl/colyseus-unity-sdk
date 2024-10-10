/* 
	Copyright (c) 2019 Denis Zykov, GameDevWare.com

	This a part of "Json & MessagePack Serialization" Unity Asset - https://www.assetstore.unity3d.com/#!/content/59918

	THIS SOFTWARE IS DISTRIBUTED "AS-IS" WITHOUT ANY WARRANTIES, CONDITIONS AND 
	REPRESENTATIONS WHETHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION THE 
	IMPLIED WARRANTIES AND CONDITIONS OF MERCHANTABILITY, MERCHANTABLE QUALITY, 
	FITNESS FOR A PARTICULAR PURPOSE, DURABILITY, NON-INFRINGEMENT, PERFORMANCE 
	AND THOSE ARISING BY STATUTE OR FROM CUSTOM OR USAGE OF TRADE OR COURSE OF DEALING.
	
	This source code is distributed via Unity Asset Store, 
	to use it in your project you should accept Terms of Service and EULA 
	https://unity3d.com/ru/legal/as_terms
*/
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

// ReSharper disable once CheckNamespace
namespace GameDevWare.Serialization.Serializers
{
	public sealed class ArraySchemaSerializer : TypeSerializer
	{
		private readonly Type classType;
		private readonly Type elementType;

		public override Type SerializedType { get { return this.classType; } }

		public ArraySchemaSerializer(Type classType)
		{
			if (classType == null) throw new ArgumentNullException("classType");

			this.classType = classType;
			this.elementType = classType.GenericTypeArguments[0];

			if (this.elementType == null) throw JsonSerializationException.TypeIsNotValid(this.GetType(), "be enumerable");
		}

		public override object Deserialize(IJsonReader reader)
		{
			if (reader == null) throw new ArgumentNullException("reader");

			if (reader.Token == JsonToken.Null)
				return null;

			var container = new ArrayList();
			if (reader.Token != JsonToken.BeginArray)
				throw JsonSerializationException.UnexpectedToken(reader, JsonToken.BeginArray);

			reader.Context.Hierarchy.Push(container);
			var i = 0;
			while (reader.NextToken() && reader.Token != JsonToken.EndOfArray)
			{
				reader.Context.Path.Push(new PathSegment(i++));

				var value = reader.ReadValue(this.elementType, false);
				container.Add(value);

				reader.Context.Path.Pop();
			}
			reader.Context.Hierarchy.Pop();

			if (reader.IsEndOfStream())
				throw JsonSerializationException.UnexpectedToken(reader, JsonToken.EndOfArray);

            object[] param =
            {
                container
            };
			return Activator.CreateInstance(this.classType, param);
		}

		public override void Serialize(IJsonWriter writer, object value)
		{
			if (writer == null) throw new ArgumentNullException("writer");
			if (value == null) throw new ArgumentNullException("value");

            MethodInfo method = classType.GetMethod("GetItems");
            var dictionary = (IDictionary)method.Invoke(value, null);

			var size = dictionary.Count;

			writer.WriteArrayBegin(size);
			var i = 0;
			foreach (var item in dictionary.Values)
			{
				writer.Context.Path.Push(new PathSegment(i++));
				writer.WriteValue(item, this.elementType);
				writer.Context.Path.Pop();
            }
			writer.WriteArrayEnd();
		}
		public override string ToString()
		{
			return string.Format("ArraySchema of {0}", this.elementType);
		}
	}
}
