// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Diagnostics.CodeAnalysis;

namespace EricksonLopez.Outbox.Storage.SqlServer;

internal sealed class OutboxMessageDataReader : IDataReader, IDataRecord
{
    private readonly ReadOnlyMemory<OutboxMessage> _records;
    private int _currentIndex = -1;

    public OutboxMessageDataReader(ReadOnlyMemory<OutboxMessage> records) => _records = records;

    public int FieldCount => 10;
    public bool Read() => ++_currentIndex < _records.Length;
    
    public object GetValue(int i)
    {
        var r = _records.Span[_currentIndex];
        return i switch
        {
            0 => r.Id,
            1 => r.MessageType,
            2 => r.Payload.ToArray(),
            3 => (object?)r.CorrelationId ?? DBNull.Value,
            4 => (object?)r.CausationId ?? DBNull.Value,
            5 => r.Headers.ToArray(),
            6 => r.Status,
            7 => r.CreatedAt,
            8 => r.CreatedAt, // updated_at
            9 => (object?)r.DeliverAt ?? DBNull.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(i))
        };
    }

    public string GetName(int i) => i switch {
        0 => "id", 1 => "type", 2 => "payload", 3 => "correlation_id", 4 => "causation_id", 5 => "headers_json", 6 => "state", 7 => "created_at", 8 => "updated_at", 9 => "deliver_at", _ => throw new ArgumentOutOfRangeException(nameof(i))
    };

    public int GetOrdinal(string name) => name switch {
        "id" => 0, "type" => 1, "payload" => 2, "correlation_id" => 3, "causation_id" => 4, "headers_json" => 5, "state" => 6, "created_at" => 7, "updated_at" => 8, "deliver_at" => 9, _ => -1
    };
    
    public void Close() { }
    public void Dispose() { }
    public int Depth => 0;
    public bool IsClosed => false;
    public int RecordsAffected => -1;
    public DataTable? GetSchemaTable() => null;
    public bool NextResult() => false;

    public bool GetBoolean(int i) => (bool)GetValue(i);
    public byte GetByte(int i) => (byte)GetValue(i);
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => 0;
    public char GetChar(int i) => (char)GetValue(i);
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => 0;
    public IDataReader GetData(int i) => throw new NotSupportedException();
    public string GetDataTypeName(int i) => "";
    public DateTime GetDateTime(int i) => (DateTime)GetValue(i);
    public decimal GetDecimal(int i) => (decimal)GetValue(i);
    public double GetDouble(int i) => (double)GetValue(i);
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public Type GetFieldType(int i) => typeof(object);
    public float GetFloat(int i) => (float)GetValue(i);
    public Guid GetGuid(int i) => (Guid)GetValue(i);
    public short GetInt16(int i) => (short)GetValue(i);
    public int GetInt32(int i) => (int)GetValue(i);
    public long GetInt64(int i) => (long)GetValue(i);
    public string GetString(int i) => (string)GetValue(i);
    public int GetValues(object[] values) => 0;
    public bool IsDBNull(int i) => GetValue(i) is DBNull;

    public object this[int i] => GetValue(i);
    public object this[string name] => GetValue(GetOrdinal(name));
}
