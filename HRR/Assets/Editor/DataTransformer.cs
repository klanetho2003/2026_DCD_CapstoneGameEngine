using Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Plastic.Newtonsoft.Json;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class DataTransformer : EditorWindow
{
#if UNITY_EDITOR
    [MenuItem("Tools/RemoveSaveData")]
    public static void RemoveSaveData()
    {
        // DB Data
        {
            string path = Path.Combine(Application.persistentDataPath, "SaveData.json");
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log("SaveFile Deleted");
            }
            else
            {
                Debug.Log("No SaveFile Detected");
            }
        }

        // Privacy Data
        {
            string privacyPath = Path.Combine(Application.persistentDataPath, "SaveAnalyticsData.json");
            if (File.Exists(privacyPath))
            {
                File.Delete(privacyPath);
                Debug.Log("Privacy Data File Deleted");
            }
            else
            {
                Debug.Log("No Privacy Data Detected");
            }
        }

        // Play Time Data
        {
            string playTimePath = Path.Combine(Application.persistentDataPath, "SavePlayTimeData.json");
            if (File.Exists(playTimePath))
            {
                File.Delete(playTimePath);
                Debug.Log("Play Time Data File Deleted");
            }
            else
            {
                Debug.Log("No Play Time Data Detected");
            }
        }

        // Permanent Data
        {
            string permanentPath = Path.Combine(Application.persistentDataPath, "PermanentData.json");
            if (File.Exists(permanentPath))
            {
                File.Delete(permanentPath);
                Debug.Log("Permanent Data File Deleted");
            }
            else
            {
                Debug.Log("No Permanent Data Detected");
            }
        }
    }

    [MenuItem("Tools/ParseExcel %#K")]
    public static void ParseExcelDataToJson()
    {
        ParseExcelDataToJson<VillagerDataLoader, VillagerData>("Villager");
        ParseExcelDataToJson<MonsterDataLoader, MonsterData>("Monster");
        ParseExcelDataToJson<NPCDataLoader, NPCData>("Npc");

        Debug.Log("DataTransformer Completed");
    }

    #region Helpers
    private static void ParseExcelDataToJson<Loader, LoaderData>(string filename) where Loader : new() where LoaderData : new()
    {
        try
        {
            Loader loader = new Loader();
            FieldInfo field = loader.GetType().GetFields()[0];
            field.SetValue(loader, ParseExcelDataToList<LoaderData>(filename));

            string jsonStr = JsonConvert.SerializeObject(loader, Formatting.Indented);
            File.WriteAllText($"{Application.dataPath}/@Resources/Data/JsonData/{filename}Data.json", jsonStr, Encoding.UTF8);
            AssetDatabase.Refresh();
            Debug.Log($"<color=green>[JsonBuilder] {filename}Data.json Created Successfully.</color>");
        }
        catch (Exception e)
        {
            // 데이터 에러 발생 시 Json 생성 중단 및 알림
            Debug.LogError($"<color=red>[JsonBuilder Failed] {filename} 변환 중 에러 발생!</color>\n{e.Message}");
        }
    }

    private static List<LoaderData> ParseExcelDataToList<LoaderData>(string filename) where LoaderData : new()
    {
        List<LoaderData> loaderDatas = new List<LoaderData>();
        string path = $"{Application.dataPath}/@Resources/Data/ExcelData/{filename}Data.csv";

        if (!File.Exists(path))
        {
            Debug.LogError($"File not found: {path}");
            return loaderDatas;
        }

        string[] lines = File.ReadAllText(path, Encoding.UTF8).Trim().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        List<string[]> rows = new List<string[]>();
        for (int l = 1; l < lines.Length; l++)
        {
            string[] row = lines[l].Replace("\r", "").Split(',');
            rows.Add(row);
        }

        for (int r = 0; r < rows.Count; r++)
        {
            string[] row = rows[r];
            if (row.Length == 0) continue;

            int colIndex = 0;

            if (!string.IsNullOrEmpty(row[0]))
            {
                LoaderData newData = new LoaderData();
                ParseRowRecursive(newData, row, ref colIndex, filename, "");
                loaderDatas.Add(newData);
            }
            else if (loaderDatas.Count > 0)
            {
                LoaderData lastData = loaderDatas[loaderDatas.Count - 1];
                ParseRowRecursive(lastData, row, ref colIndex, filename, "");
            }
        }

        return loaderDatas;
    }

    private static void ParseRowRecursive(object instance, string[] row, ref int colIndex, string filename, string parentPath)
    {
        Type type = instance.GetType();
        List<FieldInfo> fields = GetFieldsInBase(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (FieldInfo field in fields)
        {
            if (colIndex >= row.Length) break;

            Type fieldType = field.FieldType;
            string currentFieldName = string.IsNullOrEmpty(parentPath) ? field.Name : $"{parentPath}.{field.Name}";

            // 1. 리스트 타입 처리
            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type itemType = fieldType.GetGenericArguments()[0];
                IList list = field.GetValue(instance) as IList;
                if (list == null)
                {
                    list = Activator.CreateInstance(fieldType) as IList;
                    field.SetValue(instance, list);
                }

                bool isClassItem = itemType.IsClass && !itemType.IsPrimitive && itemType != typeof(string) && !itemType.IsEnum;

                if (isClassItem)
                {
                    string cellValue = row[colIndex];
                    object listItem;

                    if (!string.IsNullOrEmpty(cellValue))
                    {
                        listItem = Activator.CreateInstance(itemType);
                        list.Add(listItem);
                    }
                    else
                    {
                        if (list.Count > 0)
                            listItem = list[list.Count - 1];
                        else
                        {
                            SkipFields(itemType, ref colIndex);
                            continue;
                        }
                    }
                    ParseRowRecursive(listItem, row, ref colIndex, filename, currentFieldName);
                }
                else
                {
                    string val = row[colIndex];
                    if (!string.IsNullOrEmpty(val))
                    {
                        Debug.Log($" {filename} | {currentFieldName} -> '{val}'");
                        // [에러 처리] 변환 시도
                        try
                        {
                            object converted = ConvertValueStrict(val, itemType);
                            if (converted != null) list.Add(converted);
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"[Data Error] 파일: {filename}.csv\n위치: {currentFieldName}\n값: '{val}'\n에러: {ex.Message}");
                        }
                    }
                    colIndex++;
                }
            }
            // 2. 단순/클래스 필드 처리
            else
            {
                string cellValue = row[colIndex];

                if (fieldType.IsClass && !fieldType.IsPrimitive && fieldType != typeof(string) && !fieldType.IsEnum)
                {
                    object fieldInstance = field.GetValue(instance);
                    if (fieldInstance == null)
                    {
                        fieldInstance = Activator.CreateInstance(fieldType);
                        field.SetValue(instance, fieldInstance);
                    }
                    ParseRowRecursive(fieldInstance, row, ref colIndex, filename, currentFieldName);
                }
                else
                {
                    if (!string.IsNullOrEmpty(cellValue))
                    {
                        Debug.Log($" {filename} | {currentFieldName} -> '{cellValue}'");

                        // [에러 처리] 변환 시도
                        try
                        {
                            object convertedValue = ConvertValueStrict(cellValue, fieldType);
                            if (convertedValue != null)
                            {
                                field.SetValue(instance, convertedValue);
                            }
                        }
                        catch (Exception ex)
                        {
                            // 상위 catch 블록으로 던져서 작업을 중단시킴
                            throw new Exception($"[Data Error] 파일: {filename}.csv\n위치: {currentFieldName}\n값: '{cellValue}'\n원인: {ex.Message}");
                        }
                    }
                    colIndex++;
                }
            }
        }
    }

    private static void SkipFields(Type type, ref int colIndex)
    {
        var fields = GetFieldsInBase(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var field in fields)
        {
            if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type itemType = field.FieldType.GetGenericArguments()[0];
                SkipFields(itemType, ref colIndex);
            }
            else if (field.FieldType.IsClass && !field.FieldType.IsPrimitive && field.FieldType != typeof(string) && !field.FieldType.IsEnum)
            {
                SkipFields(field.FieldType, ref colIndex);
            }
            else
            {
                colIndex++;
            }
        }
    }

    // [핵심 수정] 엄격한 타입 변환 함수
    private static object ConvertValueStrict(string value, Type type)
    {
        if (string.IsNullOrEmpty(value)) return null;

        // 1. Bool 검사
        if (type == typeof(bool))
        {
            string v = value.Trim().ToLower();
            if (v == "true" || v == "1") return true;
            if (v == "false" || v == "0") return false;

            // 그 외의 값은 허용하지 않고 에러 발생
            throw new FormatException($"'{value}'는 유효한 bool 값(TRUE/FALSE/0/1)이 아닙니다.");
        }

        // 2. 숫자형(int, float, double) 검사
        if (type == typeof(int))
        {
            if (int.TryParse(value, out int result)) return result;
            throw new FormatException($"'{value}'는 유효한 int 정수가 아닙니다.");
        }
        if (type == typeof(float))
        {
            if (float.TryParse(value, out float result)) return result;
            throw new FormatException($"'{value}'는 유효한 float 실수가 아닙니다.");
        }
        if (type == typeof(double))
        {
            if (double.TryParse(value, out double result)) return result;
            throw new FormatException($"'{value}'는 유효한 double 실수가 아닙니다.");
        }

        // 3. Enum 검사
        if (type.IsEnum)
        {
            if (Enum.IsDefined(type, value))
            {
                return Enum.Parse(type, value);
            }
            
            try
            {
                return Enum.Parse(type, value, true); // true: 대소문자 무시 허용
            }
            catch
            {
                throw new FormatException($"'{value}'는 Enum {type.Name}에 정의되지 않은 값입니다.");
            }
        }

        // 4. 그 외 타입 (string 등)은 기존 Converter 사용하되 실패 시 에러
        try
        {
            TypeConverter converter = TypeDescriptor.GetConverter(type);
            return converter.ConvertFromString(value);
        }
        catch (Exception)
        {
            throw new FormatException($"'{value}'를 {type.Name} 형식으로 변환할 수 없습니다.");
        }
    }

    public static List<FieldInfo> GetFieldsInBase(Type type, BindingFlags bindingFlags)
    {
        List<FieldInfo> fields = new List<FieldInfo>();
        HashSet<string> fieldNames = new HashSet<string>();
        Stack<Type> stack = new Stack<Type>();

        while (type != null && type != typeof(object))
        {
            stack.Push(type);
            type = type.BaseType;
        }

        while (stack.Count > 0)
        {
            Type currentType = stack.Pop();
            foreach (var field in currentType.GetFields(bindingFlags))
            {
                if (fieldNames.Add(field.Name))
                {
                    fields.Add(field);
                }
            }
        }
        return fields;
    }
    #endregion

#endif
}
