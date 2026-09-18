using System.Collections.Generic;
using Content.Shared.GameTicking;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Fish.RoundEnd
{
    /// <summary>
    /// Единственная точка входа для подключения поиска по имени во вкладку
    /// манифеста окна итогов раунда. Вызывается одной строкой из
    /// RoundEndSummaryWindow — вся логика (создание LineEdit, фильтрация)
    /// живёт здесь и в <see cref="ManifestSearchBox"/>.
    /// </summary>
    public static class ManifestSearchHook
    {
        /// <param name="tab">Вкладка манифеста — строка поиска добавится в её начало.</param>
        /// <param name="rows">Уже построенные строки манифеста, по порядку.</param>
        /// <param name="players">Игроки в том же порядке, что и rows (для текста поиска).</param>
        public static void Apply(
            BoxContainer tab,
            IReadOnlyList<Control> rows,
            IReadOnlyList<RoundEndMessageEvent.RoundEndPlayerInfo> players)
        {
            var searchBox = new ManifestSearchBox();
            tab.AddChild(searchBox);
            searchBox.SetPositionFirst();

            var count = rows.Count < players.Count ? rows.Count : players.Count;
            for (var i = 0; i < count; i++)
            {
                var role = string.IsNullOrEmpty(players[i].Role) ? string.Empty : Loc.GetString(players[i].Role);
                var searchText = $"{players[i].PlayerOOCName} {players[i].PlayerICName} {role}";
                searchBox.Register(rows[i], searchText);
            }
        }
    }
}
