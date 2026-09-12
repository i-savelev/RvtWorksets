using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;


namespace Worksets
{
    /// <summary>
    /// Универсальный класс-обёртка для любого элемента в Revit.
    /// </summary>
    public class ObjRvt
    {
        /// <summary>
        /// Элемент Revit.
        /// </summary>
        public Element elem;

        /// <summary>
        /// Возвращает значение параметра по имени параметра.
        /// </summary>
        /// <param name="parName">Имя параметра.</param>
        /// <returns>Значение параметра или null.</returns>
        public virtual object GetParam(string parName)
        {
            Element type = elem.Document.GetElement(elem.GetTypeId());
            if (elem.LookupParameter(parName) != null)
            {
                if (elem.LookupParameter(parName).StorageType.ToString() == "String")
                    return elem.LookupParameter(parName).AsValueString();
                else if (elem.LookupParameter(parName).StorageType.ToString() == "Double")
                    return elem.LookupParameter(parName).AsDouble();
                else if (elem.LookupParameter(parName).StorageType.ToString() == "Integer")
                    return elem.LookupParameter(parName).AsInteger();
                else if (elem.LookupParameter(parName).StorageType.ToString() == "ElementId")
                    return elem.LookupParameter(parName).AsElementId();
                else return null;
            }
            else if (type != null)
            {
                if (type.LookupParameter(parName) != null)
                {
                    if (type.LookupParameter(parName).StorageType.ToString() == "String")
                    {
                        if (type.LookupParameter(parName).AsString() != null)
                            return type.LookupParameter(parName).AsValueString();
                        return type.LookupParameter(parName).AsString();
                    }
                    else if (type.LookupParameter(parName).StorageType.ToString() == "Double")
                        return type.LookupParameter(parName).AsDouble();
                    else if (type.LookupParameter(parName).StorageType.ToString() == "Integer")
                        return type.LookupParameter(parName).AsInteger();
                    else if (type.LookupParameter(parName).StorageType.ToString() == "ElementId")
                        return type.LookupParameter(parName).AsElementId();
                    else return null;
                }
                else return null;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Проверяет, заполнено ли значение параметра.
        /// </summary>
        /// <param name="parName">Имя параметра.</param>
        /// <returns>Сообщение об ошибке или null, если параметр заполнен.</returns>
        public virtual string CheckParam(string parName)
        {
            try
            {
                ElementId elemId = (ElementId)GetParam(parName);
                if (elemId.ToString() == "-1")
                    return $"«{parName}» не заполнен";
                else return null;
            }
            catch
            {
                if (GetParam(parName) != null)
                {
                    if (GetParam(parName).ToString() == "")
                        return $"«{parName}» не заполнен";
                    else if (GetParam(parName).ToString() == "0")
                        return $"«{parName}» не заполнен";
                    else return null;
                }
                else return $"«{parName}» не заполнен";
            }
        }

        /// <summary>
        /// Устанавливает значение параметра по имени параметра.
        /// </summary>
        /// <param name="parName">Имя параметра.</param>
        /// <param name="parValue">Значение параметра.</param>
        public virtual void SetParam(string parName, object parValue)
        {
            if (elem.LookupParameter(parName) != null)
            {
                if (elem.LookupParameter(parName).StorageType.ToString() == "String")
                    elem.LookupParameter(parName).Set(parValue.ToString());
                if (elem.LookupParameter(parName).StorageType.ToString() == "Double")
                {
                    var a = Convert.ToDouble(parValue);
                    elem.LookupParameter(parName).Set(a);
                }
                if (elem.LookupParameter(parName).StorageType.ToString() == "Integer")
                {
                    var a = Convert.ToInt32(parValue);
                    elem.LookupParameter(parName).Set(a);
                }
            }
        }

        /// <summary>
        /// Проверяет соответствие элемента словарям рабочих наборов.
        /// </summary>
        /// <param name="worksetDictByCat">Словарь рабочих наборов по категориям.</param>
        /// <param name="worksetDictByFamyly">Словарь рабочих наборов по семействам.</param>
        /// <returns>Сообщение о несоответствии или null.</returns>
        public virtual string CheckWorkSet(Dictionary<string, string> worksetDictByCat, Dictionary<string, string> worksetDictByFamyly)
        {
            string check = null;
            var elemTypeId = elem.GetTypeId();
            var elemType = elem.Document.GetElement(elemTypeId) as ElementType;

            // Кросс-версионное получение ID категории
            string catKey = ((BuiltInCategory)(int)elem.Category.Id.GetIdValue()).ToString();

            try
            {
                var familyPrefix = elemType?.FamilyName?.Split('_')[0] ?? "";
                var elemPrefix = elem.Name?.Split('_')[0] ?? "";

                if (worksetDictByFamyly.ContainsKey(familyPrefix) || worksetDictByFamyly.ContainsKey(elemPrefix))
                {
                    if (worksetDictByFamyly.ContainsKey(familyPrefix))
                    {
                        var val = worksetDictByFamyly[familyPrefix];
                        if (!elem.LookupParameter("Рабочий набор").AsValueString().Contains(val))
                        {
                            check = $"Элемент расположен в рабочем наборе «{elem.LookupParameter("Рабочий набор").AsValueString()}» должен находиться в рабочем наборе с «{val}» в названии";
                        }
                    }
                    else if (worksetDictByFamyly.ContainsKey(elemPrefix))
                    {
                        var val = worksetDictByFamyly[elemPrefix];
                        if (!elem.LookupParameter("Рабочий набор").AsValueString().Contains(val))
                        {
                            check = $"Элемент расположен в рабочем наборе «{elem.LookupParameter("Рабочий набор").AsValueString()}» должен находиться в рабочем наборе с «{val}» в названии";
                        }
                    }
                }
                else if (worksetDictByCat.ContainsKey(catKey))
                {
                    if (!elem.LookupParameter("Рабочий набор").AsValueString().Contains(worksetDictByCat[catKey]))
                    {
                        check = $"Элемент расположен в рабочем наборе «{elem.LookupParameter("Рабочий набор").AsValueString()}» должен находиться в рабочем наборе с «{worksetDictByCat[catKey]}» в названии";
                    }
                }
            }
            catch
            {
            }

            return check;
        }

        /// <summary>
        /// Возвращает значение параметра как строку.
        /// </summary>
        /// <param name="parName">Имя параметра.</param>
        /// <returns>Строковое значение параметра или пустая строка.</returns>
        public virtual string GetParamAsString(string parName)
        {
            Element type = elem.Document.GetElement(elem.GetTypeId());
            if (elem.LookupParameter(parName) != null)
            {
                if (elem.LookupParameter(parName).StorageType.ToString() == "String")
                {
                    if (elem.LookupParameter(parName).AsValueString() == null)
                        return "";
                    return elem.LookupParameter(parName).AsValueString();
                }
                else if (elem.LookupParameter(parName).StorageType.ToString() == "Double")
                    return elem.LookupParameter(parName).AsDouble().ToString();
                else if (elem.LookupParameter(parName).StorageType.ToString() == "Integer")
                    return elem.LookupParameter(parName).AsValueString();
                else if (elem.LookupParameter(parName).StorageType.ToString() == "ElementId")
                    return elem.LookupParameter(parName).AsValueString();
                else return "";
            }
            else if (type != null)
            {
                if (type.LookupParameter(parName) != null)
                {
                    if (type.LookupParameter(parName).StorageType.ToString() == "String")
                    {
                        if (type.LookupParameter(parName).AsString() != null)
                            return type.LookupParameter(parName).AsValueString();
                        return type.LookupParameter(parName).AsValueString();
                    }
                    else if (type.LookupParameter(parName).StorageType.ToString() == "Double")
                        return type.LookupParameter(parName).AsValueString();
                    else if (type.LookupParameter(parName).StorageType.ToString() == "Integer")
                        return type.LookupParameter(parName).AsValueString();
                    else if (type.LookupParameter(parName).StorageType.ToString() == "ElementId")
                        return type.LookupParameter(parName).AsValueString();
                    else return "";
                }
                else return "";
            }
            else
            {
                return "";
            }
        }

        /// <summary>
        /// Устанавливает цвет переопределения графики для элемента в активном виде.
        /// </summary>
        /// <param name="color">Цвет Revit.</param>
        public void Set_element_color(Color color)
        {
            FillPatternElement solid_pattern = null;
            var all_patterns = new FilteredElementCollector(elem.Document).OfClass(typeof(FillPatternElement)).ToElements();
            foreach (FillPatternElement pattern in all_patterns)
            {
                if (pattern.GetFillPattern().IsSolidFill)
                {
                    solid_pattern = pattern;
                    break;
                }
            }

            var active_view = elem.Document.ActiveView;
            var override_settings = new OverrideGraphicSettings();
            override_settings.SetSurfaceForegroundPatternColor(color);
            override_settings.SetCutForegroundPatternId(solid_pattern.Id);
            override_settings.SetCutForegroundPatternColor(color);
            override_settings.SetSurfaceForegroundPatternId(solid_pattern.Id);
            active_view.SetElementOverrides(elem.Id, override_settings);
        }

        /// <summary>
        /// Очищает переопределения графики для элемента в активном виде.
        /// </summary>
        public void Clear_overrides()
        {
            try
            {
                var active_view = elem.Document.ActiveView;
                var override_settings = new OverrideGraphicSettings();
                active_view.SetElementOverrides(elem.Id, override_settings);
            }
            catch { }
        }
    }
}