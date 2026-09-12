using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitLogger;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Serialization;
using Worksets.Forms;
using Worksets.MyObjects;
using Document = Autodesk.Revit.DB.Document;


namespace Worksets
{
    /// <summary>
    /// Команда для автоматического распределения элементов Revit по рабочим наборам 
    /// на основе настраиваемых правил (категории и параметры).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SetWorksets : IExternalCommand
    {
        //---PluginsManager---//
        /// <summary>Имя вкладки в PluginsManager.</summary>
        public static string IS_TAB_NAME => "ISTools";
        /// <summary>Отображаемое имя команды.</summary>
        public static string IS_NAME => "Рабочие наборы";
        /// <summary>Путь к embedded resource-изображению.</summary>
        public static string IS_IMAGE => "RvtWorksets.Resources.worksets.png";
        /// <summary>Описание команды для пользователя.</summary>
        public static string IS_DESCRIPTION => "Инструкция:\nСкрипт автоматически присваивает элементам рабочие наборы по заданным правилам\nПодробнее: https://github.com/i-savelev/ISTools/wiki/Рабочие-наборы";
        //---PluginsManager---//

        private readonly string _tempXmlPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "i-savelev", "Worksets", "WorksetsTemp.xml");

        private SetWorksetsForm _window;
        private List<ObjWorkset> _worksetsList;
        private List<string> _categoriesList;
        private DataTable _dtOutput;
        private Document _doc;

        /// <summary>
        /// Точка входа команды Revit.
        /// </summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            ConfigureLogging(commandData);

            try
            {
                Logger.Info("[SetWorksets] Старт команды");

                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null)
                {
                    Logger.Error("[SetWorksets] Нет активного документа");
                    message = "Нет активного документа Revit.";
                    return Result.Failed;
                }

                _doc = uidoc.Document;
                Logger.Debug($"[SetWorksets] ActiveDocument={_doc?.Title ?? "null"}");

                InitializeData();
                InitializeForm();
                SetupCategoriesGrid();
                SetupParametersGrid();
                SetupWorksetsGrid();
                SetupSettings();
                SetupEventHandlers();
                LoadXmlWhenOpen();

                _window.Show();

                Logger.Info("[SetWorksets] Форма открыта успешно");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "[SetWorksets] Ошибка выполнения команды");
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Настраивает файловый лог и снимок окружения.
        /// </summary>
        private void ConfigureLogging(ExternalCommandData commandData)
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Temp", "i-savelev", "Worksets");

            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            Logger.SetLogPath(Path.Combine(logDir, "worksets.log"));
            Logger.SetLogLevel(Logger.LogLevel.Debug);
            Logger.Init(
                hostName: "Autodesk Revit",
                hostVersionNumber: commandData.Application.Application.VersionNumber,
                hostBuild: commandData.Application.Application.VersionBuild,
                hasActiveDocument: commandData.Application.ActiveUIDocument != null);
        }

        /// <summary>
        /// Инициализирует начальные данные команды (категории, DataTable).
        /// </summary>
        private void InitializeData()
        {
            _worksetsList = new List<ObjWorkset>();
            _dtOutput = new DataTable();
            _dtOutput.Columns.Add("1");

            var allCategories = _doc.Settings.Categories;
            _categoriesList = new List<string>();

            foreach (Category category in allCategories)
            {
                _dtOutput.Rows.Add($"{category.Name} - {(BuiltInCategory)(int)category.Id.GetIdValue()}");
                if (category.CategoryType == CategoryType.Model)
                    _categoriesList.Add(category.Name);
            }

            _categoriesList.Add("Оси");
            _categoriesList.Add("Уровни");
            _categoriesList.Add("Выступающие профили");
            _categoriesList.Sort();

            Logger.Debug($"[SetWorksets] Инициализировано категорий модели: {_categoriesList.Count}");
        }

        /// <summary>
        /// Инициализирует и настраивает основную форму.
        /// </summary>
        private void InitializeForm()
        {
            _window = new SetWorksetsForm { Text = "Настройка рабочих наборов" };
            _window.groupBox1.Text = "Категории";
            _window.groupBox2.Text = "Параметры";
            _window.groupBox3.Text = "Настройки";
            _window.groupBox4.Text = "Рабочие наборы";
            _window.groupBox5.Text = "Информация";
            _window.textBox2.Text = "Данный скрипт позволяет автоматически распределять элементы по рабочим наборам.";
        }

        /// <summary>
        /// Настраивает таблицу категорий.
        /// </summary>
        private void SetupCategoriesGrid()
        {
            var cmb = new DataGridViewComboBoxColumn
            {
                HeaderText = "Категория",
                DataSource = _categoriesList
            };
            _window.dataGridView1.Columns.Add(cmb);
            _window.button7.Text = "+";
            _window.button8.Text = "-";
        }

        /// <summary>
        /// Настраивает таблицу параметров.
        /// </summary>
        private void SetupParametersGrid()
        {
            var parameterConditionList = new List<string> { "равно", "содержит", "не содержит", "не равно" };
            var parameterBoolConditionList = new List<string> { "и", "или" };

            var columnParameterName = new DataGridViewColumn
            {
                HeaderText = "Название параметра",
                CellTemplate = new DataGridViewTextBoxCell()
            };
            var columnParameterCondition = new DataGridViewComboBoxColumn
            {
                DataSource = parameterConditionList,
                HeaderText = "Условие"
            };
            var columnParameterBoolCondition = new DataGridViewComboBoxColumn
            {
                DataSource = parameterBoolConditionList,
                HeaderText = "и/или"
            };
            var columnParameterValue = new DataGridViewColumn
            {
                HeaderText = "Значение параметра",
                CellTemplate = new DataGridViewTextBoxCell()
            };

            _window.dataGridView2.Columns.Add(columnParameterBoolCondition);
            _window.dataGridView2.Columns.Add(columnParameterName);
            _window.dataGridView2.Columns.Add(columnParameterCondition);
            _window.dataGridView2.Columns.Add(columnParameterValue);
            _window.dataGridView2.Columns[0].Width = 60;
            _window.dataGridView2.Columns[2].Width = 120;

            _window.button4.Text = "-";
            _window.button2.Text = "+";
        }

        /// <summary>
        /// Настраивает таблицу рабочих наборов.
        /// </summary>
        private void SetupWorksetsGrid()
        {
            var columnWorksetName = new DataGridViewColumn
            {
                HeaderText = "Название рабочего набора",
                CellTemplate = new DataGridViewTextBoxCell()
            };
            _window.dataGridView3.Columns.Add(columnWorksetName);
            _window.button1.Text = "Добавить";
            _window.button5.Text = "Удалить";
            _window.button10.Text = "Переименовать";
            _window.button12.Text = "Копировать";
        }

        /// <summary>
        /// Настраивает блок настроек.
        /// </summary>
        private void SetupSettings()
        {
            var parameterBoolConditionList_2 = new List<string> { "и", "или" };
            _window.comboBox1.DataSource = parameterBoolConditionList_2;
            _window.button3.Text = "Сохранить рабочий набор";
            _window.button6.Text = "Сохранить шаблон";
            _window.button9.Text = "Загрузить шаблон";
            _window.button11.Text = "Распределить элементы по наборам";
        }

        /// <summary>
        /// Подключает обработчики событий UI.
        /// </summary>
        private void SetupEventHandlers()
        {
            _window.button7.Click += (s, e) => IsUtils.AddRow(_window.dataGridView1);
            _window.button8.Click += (s, e) => IsUtils.DeleteRow(_window.dataGridView1);
            _window.button4.Click += (s, e) => IsUtils.DeleteRow(_window.dataGridView2);
            _window.button2.Click += (s, e) => IsUtils.AddRow(_window.dataGridView2);
            _window.button5.Click += (s, e) => DeleteWorkSet();
            _window.button1.Click += (s, e) => AddWorksetDialog();
            _window.button10.Click += (s, e) => RenameWorkSetDialog();
            _window.button12.Click += (s, e) => CopyWorkset();
            _window.button3.Click += (s, e) => SaveWorkset();
            _window.button6.Click += (s, e) => SaveXml();
            _window.button9.Click += (s, e) => LoadXml();

            var handler = new IsHandlerSwitch(app => SetWsToElement());
            _window.button11.Click += (s, e) => handler.ExternalEvent.Raise();

            _window.dataGridView3.CellClick += SetDataToOtherDatagrid;

            try
            {
                _window.FormClosed += (s, e) => SavingWhenClosing();
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "[SetWorksets] Ошибка при подписке на FormClosed");
            }
        }

        /// <summary>
        /// Заполняет DataGridView (категории и параметры) при выборе рабочего набора.
        /// </summary>
        private void SetDataToOtherDatagrid(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            _window.dataGridView2.Rows.Clear();
            _window.dataGridView1.Rows.Clear();

            var rowIndex = e.RowIndex;
            var worksetName = _window.dataGridView3[0, rowIndex].Value?.ToString();

            foreach (var objWs in _worksetsList)
            {
                if (worksetName != null && worksetName == objWs.Name)
                {
                    if (objWs.Conditions != null)
                    {
                        foreach (var cond in objWs.Conditions)
                            _window.dataGridView2.Rows.Add(cond.GetStrings());
                    }

                    if (objWs.Categories != null)
                    {
                        foreach (var cat in objWs.Categories)
                            _window.dataGridView1.Rows.Add(cat.GetStrings());
                    }

                    if (objWs.CategoriesAndParams != null)
                        _window.comboBox1.Text = objWs.CategoriesAndParams;
                }
            }
        }

        /// <summary>
        /// Выполняет переименование рабочего набора.
        /// </summary>
        private void RenameWorkset(InputDialogForm inputWindow)
        {
            string name = "";
            foreach (DataGridViewRow row in _window.dataGridView3.SelectedRows)
            {
                name = _window.dataGridView3.Rows[row.Index].Cells[0].Value?.ToString() ?? "";
            }

            foreach (var ws in _worksetsList)
            {
                if (ws.Name == name)
                {
                    ws.Name = inputWindow.textBox2.Text;
                    foreach (DataGridViewRow row in _window.dataGridView3.SelectedRows)
                    {
                        _window.dataGridView3.Rows[row.Index].Cells[0].Value = inputWindow.textBox2.Text;
                    }
                    inputWindow.Close();
                    Logger.Info($"[SetWorksets] Рабочий набор переименован: '{name}' -> '{ws.Name}'");
                }
            }
        }

        /// <summary>
        /// Открывает диалог переименования рабочего набора.
        /// </summary>
        private void RenameWorkSetDialog()
        {
            string name = "";
            foreach (DataGridViewRow row in _window.dataGridView3.SelectedRows)
            {
                name = _window.dataGridView3.Rows[row.Index].Cells[0].Value?.ToString() ?? "";
            }

            var inputWindow = new InputDialogForm
            {
                Text = "Переименование"
            };
            inputWindow.textBox1.Text = "Укажите название рабочего набора";
            inputWindow.textBox2.Text = name;
            inputWindow.button1.Text = "Ок";
            inputWindow.button1.Click += (s, e) => RenameWorkset(inputWindow);
            inputWindow.ShowDialog();
        }

        /// <summary>
        /// Сохраняет текущий рабочий набор из UI в список.
        /// Валидирует категории: пропускает пустые строки и категории без найденного BuiltInCategory.
        /// </summary>
        private void SaveWorkset()
        {
            string name = "";
            foreach (DataGridViewRow row in _window.dataGridView3.SelectedRows)
            {
                name = _window.dataGridView3.Rows[row.Index].Cells[0].Value?.ToString() ?? "";
            }

            var conditionsList = new List<ObjParamCondition>();
            for (int i = 0; i < _window.dataGridView2.Rows.Count; i++)
            {
                var boolCondition = _window.dataGridView2.Rows[i].Cells[0].Value?.ToString() ?? "";
                var paramName = _window.dataGridView2.Rows[i].Cells[1].Value?.ToString() ?? "";
                var condition = _window.dataGridView2.Rows[i].Cells[2].Value?.ToString() ?? "";
                var paramValue = _window.dataGridView2.Rows[i].Cells[3].Value?.ToString() ?? "";
                conditionsList.Add(new ObjParamCondition(boolCondition, paramName, condition, paramValue));
            }

            var categoryList = new List<ObjCategory>();
            var allCategories = _doc.Settings.Categories;

            for (int i = 0; i < _window.dataGridView1.Rows.Count; i++)
            {
                var categoryName = _window.dataGridView1.Rows[i].Cells[0].Value?.ToString() ?? "";

                if (string.IsNullOrWhiteSpace(categoryName))
                {
                    Logger.Debug($"[SetWorksets] Пропуск пустой строки категории в DataGridView");
                    continue;
                }

                var objCat = new ObjCategory(categoryName);
                bool found = false;

                if (categoryName == "Выступающие профили")
                {
                    objCat.BuiltIn = BuiltInCategory.OST_Cornices;
                    found = true;
                }
                else
                {
                    foreach (Category cat in allCategories)
                    {
                        if (categoryName == cat.Name)
                        {
                            objCat.BuiltIn = (BuiltInCategory)(int)cat.Id.GetIdValue();
                            found = true;
                            break;
                        }
                    }
                }

                if (found)
                {
                    categoryList.Add(objCat);
                }
                else
                {
                    Logger.Warning($"[SetWorksets] Категория '{categoryName}' не найдена в документе, пропуск");
                }
            }

            foreach (var objWs in _worksetsList)
            {
                if (name == objWs.Name)
                {
                    objWs.Conditions = conditionsList;
                    objWs.Categories = categoryList;
                    objWs.CategoriesAndParams = _window.comboBox1.Text;
                    Logger.Info($"[SetWorksets] Сохранен рабочий набор '{name}' (категорий: {categoryList.Count}, условий: {conditionsList.Count})");
                }
            }
        }

        /// <summary>
        /// Удаляет выбранный рабочий набор из UI и из списка.
        /// </summary>
        private void DeleteWorkSet()
        {
            foreach (DataGridViewRow row in _window.dataGridView3.SelectedRows)
            {
                var name = row.Cells[0].Value?.ToString() ?? "";
                _window.dataGridView3.Rows.Remove(row);
                for (int i = _worksetsList.Count - 1; i >= 0; i--)
                {
                    if (_worksetsList[i].Name == name)
                    {
                        _worksetsList.RemoveAt(i);
                        Logger.Debug($"[SetWorksets] Удален рабочий набор: {name}");
                    }
                }
            }
        }

        /// <summary>
        /// Открывает диалог добавления нового рабочего набора.
        /// </summary>
        private void AddWorksetDialog()
        {
            var inputWindow = new InputDialogForm { Text = "Добавление рабочего набора" };
            inputWindow.textBox1.Text = "Укажите название рабочего набора";
            inputWindow.button1.Text = "Ок";
            inputWindow.button1.Click += (s, e) => AddWorkset(inputWindow);
            inputWindow.ShowDialog();
        }

        /// <summary>
        /// Добавляет новый рабочий набор в список и UI.
        /// </summary>
        private void AddWorkset(InputDialogForm inputWindow)
        {
            var name = inputWindow.textBox2.Text;
            if (string.IsNullOrWhiteSpace(name))
            {
                TaskDialog.Show("Предупреждение", "Название не может быть пустым");
                Logger.Warning("[SetWorksets] Попытка создать рабочий набор с пустым именем");
                return;
            }

            bool exists = _worksetsList.Any(obj => obj.Name == name);
            if (exists)
            {
                TaskDialog.Show("Предупреждение", "Рабочий набор с таким названием уже есть");
                Logger.Warning($"[SetWorksets] Рабочий набор с именем '{name}' уже существует");
                return;
            }

            var objWs = new ObjWorkset(name);
            _worksetsList.Add(objWs);
            _window.dataGridView3.Rows.Add(new string[] { objWs.Name });
            inputWindow.Close();
            Logger.Info($"[SetWorksets] Создан рабочий набор: '{name}'");
        }

        /// <summary>
        /// Сохраняет рабочие наборы в XML через диалог выбора файла.
        /// </summary>
        private void SaveXml()
        {
            var objWorksets = _worksetsList.ToArray();
            var serializer = new XmlSerializer(typeof(ObjWorkset[]));

            var saveFileDialog = new SaveFileDialog
            {
                Title = "Выберите папку для сохранения шаблона",
                Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                DefaultExt = ".xml",
                FileName = "Рабочие наборы.xml"
            };

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    using (var streamWriter = new StreamWriter(saveFileDialog.FileName))
                    {
                        serializer.Serialize(streamWriter, objWorksets);
                    }
                    Logger.Info($"[SetWorksets] Шаблон сохранен в {saveFileDialog.FileName} (наборов: {objWorksets.Length})");
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, "[SetWorksets] Ошибка при сохранении XML");
                    TaskDialog.Show("Ошибка", $"Не удалось сохранить файл:\n{ex.Message}");
                }
            }
        }

        /// <summary>
        /// Сохраняет рабочие наборы во временный XML при закрытии формы.
        /// </summary>
        private void SavingWhenClosing()
        {
            try
            {
                var objWorksets = _worksetsList.ToArray();
                var serializer = new XmlSerializer(typeof(ObjWorkset[]));

                var dir = Path.GetDirectoryName(_tempXmlPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (var streamWriter = new StreamWriter(_tempXmlPath))
                {
                    serializer.Serialize(streamWriter, objWorksets);
                }
                Logger.Debug($"[SetWorksets] Временный шаблон сохранен: {_tempXmlPath} (наборов: {objWorksets.Length})");
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "[SetWorksets] Ошибка при автосохранении временного XML");
            }
        }

        /// <summary>
        /// Загружает рабочие наборы из временного XML при открытии.
        /// Проверяет файл на существование и ненулевой размер.
        /// </summary>
        private void LoadXmlWhenOpen()
        {
            try
            {
                if (!File.Exists(_tempXmlPath))
                {
                    Logger.Debug("[SetWorksets] Временный XML не найден, пропуск автозагрузки");
                    return;
                }

                var fileInfo = new FileInfo(_tempXmlPath);
                if (fileInfo.Length == 0)
                {
                    Logger.Warning("[SetWorksets] Временный XML пустой, удаляю файл");
                    File.Delete(_tempXmlPath);
                    return;
                }

                _window.dataGridView3.Rows.Clear();
                _worksetsList.Clear();

                var serializer = new XmlSerializer(typeof(ObjWorkset[]));
                using (var streamReader = new StreamReader(_tempXmlPath))
                {
                    var objWorksets = serializer.Deserialize(streamReader) as ObjWorkset[];
                    if (objWorksets != null)
                    {
                        _worksetsList = objWorksets.OrderBy(p => p.Name).ToList();
                        foreach (var objWs in _worksetsList)
                        {
                            _window.dataGridView3.Rows.Add(new string[] { objWs.Name });
                        }
                        Logger.Info($"[SetWorksets] Загружено наборов из временного XML: {_worksetsList.Count}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "[SetWorksets] Ошибка при загрузке временного XML");
            }
        }

        /// <summary>
        /// Копирует выбранный рабочий набор.
        /// </summary>
        private void CopyWorkset()
        {
            string name = "";
            foreach (DataGridViewRow row in _window.dataGridView3.SelectedRows)
            {
                name = _window.dataGridView3.Rows[row.Index].Cells[0].Value?.ToString() ?? "";
            }

            foreach (var objWs in _worksetsList)
            {
                if (name == objWs.Name)
                {
                    var newObjWs = objWs.Clone(objWs);
                    _worksetsList.Add(newObjWs);
                    _window.dataGridView3.Rows.Add(new string[] { newObjWs.Name });
                    Logger.Info($"[SetWorksets] Скопирован рабочий набор: '{name}' -> '{newObjWs.Name}'");
                    break;
                }
            }
        }

        /// <summary>
        /// Загружает рабочие наборы из XML через диалог выбора файла.
        /// </summary>
        private void LoadXml()
        {
            var openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    _window.dataGridView3.Rows.Clear();
                    _worksetsList.Clear();

                    var serializer = new XmlSerializer(typeof(ObjWorkset[]));
                    using (var streamReader = new StreamReader(openFileDialog.FileName))
                    {
                        var objWorksets = serializer.Deserialize(streamReader) as ObjWorkset[];
                        if (objWorksets != null)
                        {
                            _worksetsList = objWorksets.OrderBy(p => p.Name).ToList();
                            foreach (var objWs in _worksetsList)
                            {
                                _window.dataGridView3.Rows.Add(new string[] { objWs.Name });
                            }
                            Logger.Info($"[SetWorksets] Загружено наборов из {openFileDialog.FileName}: {_worksetsList.Count}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, "[SetWorksets] Ошибка при загрузке XML");
                    TaskDialog.Show("Ошибка", $"Не удалось загрузить файл:\n{ex.Message}");
                }
            }
        }

        /// <summary>
        /// Основная логика распределения элементов по рабочим наборам.
        /// </summary>
        private void SetWsToElement()
        {
            try
            {
                _dtOutput.Clear();
                _window.progressBar1.Value = 0;
                _window.progressBar1.Maximum = _worksetsList.Count;
                _window.progressBar1.Step = 1;

                var stopwatch = new Stopwatch();
                stopwatch.Start();

                Logger.Info($"[SetWorksets] Начало распределения элементов. Наборов: {_worksetsList.Count}");

                var allCategories = _doc.Settings.Categories;
                var allBuiltinCategoriesFilterList = new List<BuiltInCategory>();

                foreach (Category category in allCategories)
                {
                    if (category.CategoryType == CategoryType.Model)
                    {
                        var builtIn = (BuiltInCategory)(int)category.Id.GetIdValue();
                        if (builtIn != BuiltInCategory.OST_Materials &&
                            builtIn != BuiltInCategory.OST_PipeSegments &&
                            builtIn != BuiltInCategory.OST_ProjectInformation &&
                            builtIn != BuiltInCategory.OST_DetailComponents &&
                            builtIn != BuiltInCategory.OST_Schedules &&
                            builtIn != BuiltInCategory.OST_Sheets &&
                            builtIn != BuiltInCategory.OST_Lines &&
                            builtIn != BuiltInCategory.INVALID)
                        {
                            allBuiltinCategoriesFilterList.Add(builtIn);
                        }
                    }
                }
                allBuiltinCategoriesFilterList.Add(BuiltInCategory.OST_Grids);
                allBuiltinCategoriesFilterList.Add(BuiltInCategory.OST_Levels);
                allBuiltinCategoriesFilterList.Add(BuiltInCategory.OST_Cornices);
                allBuiltinCategoriesFilterList.Add(BuiltInCategory.OST_IOSModelGroups);

                var allBuiltinCategoriesFilter = allBuiltinCategoriesFilterList.ToArray();
                var allElements = new FilteredElementCollector(_doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(allBuiltinCategoriesFilter))
                    .Cast<Element>()
                    .ToList();

                Logger.Debug($"[SetWorksets] Собрано элементов для обработки: {allElements.Count}");

                using (var transaction = new Transaction(_doc))
                {
                    transaction.Start("Create Workset");

                    foreach (var objWs in _worksetsList)
                    {
                        _window.progressBar1.PerformStep();
                        _dtOutput.Rows.Add($"############# Набор: {objWs.Name} #################");
                        Logger.Debug($"[SetWorksets] Обработка набора: '{objWs.Name}'");

                        var userWorksets = new FilteredWorksetCollector(_doc).OfKind(WorksetKind.UserWorkset);
                        var unicWorkset = userWorksets.FirstOrDefault(ws => ws.Name.Equals(objWs.Name));

                        if (unicWorkset == null)
                        {
                            var newWorkset = Workset.Create(_doc, objWs.Name);
                            Logger.Debug($"[SetWorksets] Создан новый рабочий набор: '{objWs.Name}' (Id={newWorkset.Id.GetIdValue()})");
                            userWorksets = new FilteredWorksetCollector(_doc).OfKind(WorksetKind.UserWorkset);
                            unicWorkset = userWorksets.FirstOrDefault(ws => ws.Name.Equals(objWs.Name));
                        }

                        if (unicWorkset == null)
                        {
                            Logger.Warning($"[SetWorksets] Не удалось найти/создать рабочий набор '{objWs.Name}'");
                            continue;
                        }

                        var worksetId = unicWorkset.Id;

                        if (objWs.Categories.Count == 0 && objWs.Conditions.Count == 0)
                        {
                            Logger.Debug($"[SetWorksets] Пропуск набора '{objWs.Name}': нет категорий и условий");
                            continue;
                        }

                        var listElementsToSetWorkset = allElements;
                        var builtinCategoriesFilterList = new List<BuiltInCategory>();

                        if (objWs.Categories.Count > 0)
                        {
                            foreach (var category in objWs.Categories)
                                builtinCategoriesFilterList.Add(category.BuiltIn);

                            var builtinCategoriesFilter = builtinCategoriesFilterList.ToArray();
                            var elementsByCategory = new FilteredElementCollector(_doc)
                                .WherePasses(new ElementMulticategoryFilter(builtinCategoriesFilter))
                                .WhereElementIsNotElementType()
                                .Cast<Element>()
                                .ToList();

                            Logger.Debug($"[SetWorksets] Набор '{objWs.Name}': элементов по категориям: {elementsByCategory.Count}");

                            if (objWs.CategoriesAndParams == "и")
                                listElementsToSetWorkset = elementsByCategory;
                            else if (objWs.CategoriesAndParams == "или")
                                listElementsToSetWorkset = allElements;
                        }

                        Logger.Debug($"[SetWorksets] Набор '{objWs.Name}': элементов для проверки: {listElementsToSetWorkset.Count}");

                        int setCount = 0;
                        foreach (var element in listElementsToSetWorkset)
                        {
                            var objRvt = new ObjRvt { elem = element };
                            var catName = element.Category?.Name ?? "Без категории";
                            var elemName = element.Name ?? "Без имени";
                            _dtOutput.Rows.Add($"### Набор: {objWs.Name}. Элемент {catName}: {elemName}");

                            bool boolResult = false;
                            string conditionLog = "";
                            bool matchedByCategory = false;

                            if (objWs.CategoriesAndParams == "или")
                            {
                                var elemBuiltInCat = (BuiltInCategory)(int)element.Category.Id.GetIdValue();
                                if (builtinCategoriesFilterList.Contains(elemBuiltInCat))
                                {
                                    boolResult = true;
                                    matchedByCategory = true;
                                }
                                else
                                {
                                    boolResult = ParameterCheck(objRvt, objWs, out conditionLog);
                                }
                            }
                            else if (objWs.CategoriesAndParams == "и")
                            {
                                boolResult = ParameterCheck(objRvt, objWs, out conditionLog);
                            }

                            if (objWs.Conditions.Count == 0)
                                boolResult = true;

                            if (boolResult)
                            {
                                _dtOutput.Rows.Add($"------------{boolResult}");

                                bool setSuccess;
                                if (element is RevitLinkInstance linkInstance)
                                {
                                    setSuccess = SetLinkWorksets(linkInstance, worksetId);
                                }
                                else
                                {
                                    setSuccess = SetElementWorkset(element, worksetId);
                                }

                                if (setSuccess)
                                {
                                    setCount++;

                                    // Компактное логирование только для назначенных элементов
                                    string logLine = $"{{ id={element.Id.GetIdValue()} }} {{ \"Категория\" = \"{catName}\" }}";
                                    if (element is RevitLinkInstance)
                                    {
                                        logLine += " [RevitLink Экземпляр]";
                                    }
                                    if (matchedByCategory)
                                    {
                                        logLine += " [по категории]";
                                    }
                                    else if (objWs.Conditions.Count == 0)
                                    {
                                        logLine += " [без условий]";
                                    }
                                    else
                                    {
                                        logLine += $" {objWs.CategoriesAndParams} {conditionLog}";
                                    }
                                    Logger.Info($"[SetWorksets] Назначен: {logLine}");
                                }
                            }
                        }
                        Logger.Debug($"[SetWorksets] Набор '{objWs.Name}': назначено рабочих наборов: {setCount}");
                    }
                    transaction.Commit();
                }

                stopwatch.Stop();
                var elapsedSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 3);
                _window.textBox1.Text = $"Время выполнения: {elapsedSeconds} сек.";
                _window.progressBar1.Value = _worksetsList.Count;
                _window.dataGridDebug.DataSource = _dtOutput;

                Logger.Info($"[SetWorksets] Распределение завершено за {elapsedSeconds} сек.");
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "[SetWorksets] Критическая ошибка в SetWsToElement");
                TaskDialog.Show("Ошибка", $"Не удалось распределить элементы:\n{ex.Message}");
            }
        }

        /// <summary>
        /// Устанавливает рабочий набор для обычного элемента (не RevitLink).
        /// </summary>
        /// <param name="element">Элемент для обработки.</param>
        /// <param name="worksetId">Целевой ID рабочего набора.</param>
        /// <returns>True, если рабочий набор успешно установлен.</returns>
        private bool SetElementWorkset(Element element, WorksetId worksetId)
        {
            var wsParam = element.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
            try
            {
                wsParam.Set((int)worksetId.GetIdValue());
                return true;
            }
            catch (Exception ex)
            {
                Logger.Debug($"[SetWorksets] Не удалось установить рабочий набор для элемента {element.Id.GetIdValue()}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Устанавливает рабочий набор для экземпляра связи (RevitLinkInstance) и её типа (RevitLinkType).
        /// Если тип только для чтения — логирует предупреждение и пропускает тип, но экземпляр всё равно обрабатывается.
        /// </summary>
        /// <param name="linkInstance">Экземпляр связи.</param>
        /// <param name="worksetId">Целевой ID рабочего набора.</param>
        /// <returns>True, если рабочий набор успешно установлен хотя бы для экземпляра.</returns>
        private bool SetLinkWorksets(RevitLinkInstance linkInstance, WorksetId worksetId)
        {
            bool instanceSuccess = false;
            bool typeSuccess = false;

            // Получение типа связи
            var linkType = _doc.GetElement(linkInstance.GetTypeId()) as RevitLinkType;
            var linkTypeName = linkType?.Name ?? "Неизвестный тип";

            // 🔹 Обработка экземпляра связи (RevitLinkInstance)
            var instanceWorksetParam = linkInstance.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
            if (instanceWorksetParam != null && !instanceWorksetParam.IsReadOnly)
            {
                try
                {
                    instanceWorksetParam.Set((int)worksetId.GetIdValue());
                    instanceSuccess = true;
                    Logger.Debug($"[SetWorksets] [RevitLink] Экземпляр '{linkTypeName}' (Id={linkInstance.Id.GetIdValue()}) перемещен в набор");
                }
                catch (Exception ex)
                {
                    Logger.Debug($"[SetWorksets] [RevitLink] Ошибка установки набора для экземпляра {linkInstance.Id.GetIdValue()}: {ex.Message}");
                }
            }
            else
            {
                Logger.Warning($"[SetWorksets] [RevitLink] Экземпляр '{linkTypeName}' (Id={linkInstance.Id.GetIdValue()}) — только для чтения, пропуск");
            }

            // 🔹 Обработка типа связи (RevitLinkType)
            if (linkType != null)
            {
                var typeWorksetParam = linkType.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (typeWorksetParam != null && !typeWorksetParam.IsReadOnly)
                {
                    try
                    {
                        typeWorksetParam.Set((int)worksetId.GetIdValue());
                        typeSuccess = true;
                        Logger.Debug($"[SetWorksets] [RevitLink] Тип '{linkTypeName}' (Id={linkType.Id.GetIdValue()}) перемещен в набор");
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"[SetWorksets] [RevitLink] Ошибка установки набора для типа {linkType.Id.GetIdValue()}: {ex.Message}");
                    }
                }
                else
                {
                    Logger.Warning($"[SetWorksets] [RevitLink] Тип '{linkTypeName}' (Id={linkType.Id.GetIdValue()}) — только для чтения, пропуск");
                }
            }
            else
            {
                Logger.Warning($"[SetWorksets] [RevitLink] Не удалось получить RevitLinkType для экземпляра {linkInstance.Id.GetIdValue()}");
            }

            return instanceSuccess;
        }

        /// <summary>
        /// Проверяет соответствие параметров элемента условиям рабочего набора.
        /// </summary>
        /// <param name="objRvt">Обёртка элемента.</param>
        /// <param name="objWs">Рабочий набор с условиями.</param>
        /// <param name="conditionLog">Строка с описанием условий для логирования.</param>
        /// <returns>True, если элемент соответствует условиям.</returns>
        private bool ParameterCheck(ObjRvt objRvt, ObjWorkset objWs, out string conditionLog)
        {
            var andCondition = new List<bool>();
            var orCondition = new List<bool>();
            var conditionParts = new List<string>();

            for (int i = 0; i < objWs.Conditions.Count; i++)
            {
                var param = objWs.Conditions[i];
                bool paramCondition = false;
                var parameterValue = objRvt.GetParamAsString(param.ParamName);

                switch (param.Condition)
                {
                    case "равно":
                        paramCondition = parameterValue == param.ParamValue;
                        break;
                    case "содержит":
                        try { paramCondition = parameterValue.Contains(param.ParamValue); } catch { }
                        break;
                    case "не равно":
                        paramCondition = !(parameterValue == param.ParamValue);
                        break;
                    case "не содержит":
                        try { paramCondition = !parameterValue.Contains(param.ParamValue); } catch { }
                        break;
                }

                if (param.BoolCondition == "и")
                    andCondition.Add(paramCondition);
                else
                    orCondition.Add(paramCondition);

                string part = $"\"{param.ParamName}\" {param.Condition} \"{param.ParamValue}\"";
                if (i == 0)
                    conditionParts.Add(part);
                else
                    conditionParts.Add($"{param.BoolCondition} {part}");
            }

            bool andResult = andCondition.Count > 0 ? andCondition.All(c => c) : true;
            bool orResult = orCondition.Count > 0 ? orCondition.Any(c => c) : true;

            bool boolResult = andResult & orResult;

            conditionLog = conditionParts.Count > 0 ? $"{{{string.Join(" ", conditionParts)}}}" : "";

            return boolResult;
        }
    }
}