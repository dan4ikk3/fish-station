using System.Collections.Generic;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Fish.RoundEnd
{
    /// <summary>
    /// Строка поиска для вкладки манифеста экипажа в окне итогов раунда.
    /// Не занимается получением данных о игроках — только скрывает/показывает
    /// уже созданные строки манифеста (<see cref="Register"/>) по подстроке имени.
    /// </summary>
    public sealed class ManifestSearchBox : BoxContainer
    {
        private readonly List<(Control Row, string SearchText)> _entries = new();
        private readonly LineEdit _search;

        public ManifestSearchBox()
        {
            Orientation = LayoutOrientation.Horizontal;
            Margin = new Thickness(0, 0, 0, 6);

            _search = new LineEdit
            {
                HorizontalExpand = true,
                PlaceHolder = Loc.GetString("fish-manifest-search-placeholder"),
            };
            _search.OnTextChanged += _ => ApplyFilter();

            AddChild(_search);
        }

        /// <summary>
        /// Регистрирует строку манифеста для последующей фильтрации.
        /// Вызывать сразу после создания строки, до применения фильтра.
        /// </summary>
        /// <param name="row">Контрол строки (обычно горизонтальный BoxContainer с иконкой и текстом).</param>
        /// <param name="searchText">Текст, по которому строка должна находиться (OOC-имя, IC-имя и т.д.).</param>
        public void Register(Control row, string searchText)
        {
            _entries.Add((row, searchText.ToLowerInvariant()));

            // Применяем текущий фильтр сразу, чтобы порядок регистрации не имел значения.
            var query = _search.Text.Trim().ToLowerInvariant();
            row.Visible = query.Length == 0 || ContainsQuery(searchText, query);
        }

        private void ApplyFilter()
        {
            var query = _search.Text.Trim().ToLowerInvariant();

            foreach (var (row, searchText) in _entries)
            {
                row.Visible = query.Length == 0 || ContainsQuery(searchText, query);
            }
        }

        private static bool ContainsQuery(string searchText, string query)
        {
            return searchText.ToLowerInvariant().Contains(query);
        }
    }
}
