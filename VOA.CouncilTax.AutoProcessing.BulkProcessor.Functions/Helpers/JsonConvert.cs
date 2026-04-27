using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace VOA.CouncilTax.AutoProcessing.Helpers;

public static class JsonConvert
{
	public static string Serialize<T>(T obj)
	{
		using var stream = new MemoryStream();
		GetSerializer<T>().WriteObject(stream, obj);
		return Encoding.UTF8.GetString(stream.ToArray());
	}

	public static T Deserialize<T>(string json)
	{
		using var stream = new MemoryStream();
		using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
		writer.Write(json);
		writer.Flush();
		stream.Position = 0;
		return (T)GetSerializer<T>().ReadObject(stream)!;
	}

	public static T Deserialize<T>(string json, string dateTimeFormat)
	{
		using var stream = new MemoryStream();
		using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
		writer.Write(json);
		writer.Flush();
		stream.Position = 0;
		return (T)GetSerializer<T>(dateTimeFormat).ReadObject(stream)!;
	}

	private static DataContractJsonSerializer GetSerializer<T>()
	{
		var settings = new DataContractJsonSerializerSettings
		{
			UseSimpleDictionaryFormat = true,
			DateTimeFormat = new DateTimeFormat("u"),
		};

		return new DataContractJsonSerializer(typeof(T), settings);
	}

	private static DataContractJsonSerializer GetSerializer<T>(string dateTimeFormat)
	{
		var settings = new DataContractJsonSerializerSettings
		{
			UseSimpleDictionaryFormat = true,
			DateTimeFormat = new DateTimeFormat(dateTimeFormat),
		};

		return new DataContractJsonSerializer(typeof(T), settings);
	}
}
