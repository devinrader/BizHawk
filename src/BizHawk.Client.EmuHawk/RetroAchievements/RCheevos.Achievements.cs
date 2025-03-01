using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;

namespace BizHawk.Client.EmuHawk
{
	public partial class RCheevos
	{
		private readonly RCheevosAchievementListForm _cheevoListForm = new();

		private class CheevoUnlockTask
		{
			private LibRCheevos.rc_api_award_achievement_request_t _apiParams;
			public Task Task { get; private set; }
			public bool Success { get; private set; }

			private void CheevoUnlockTaskCallback(byte[] serv_resp)
			{
				var res = _lib.rc_api_process_award_achievement_response(out var resp, serv_resp);
				_lib.rc_api_destroy_award_achievement_response(ref resp);
				Success = res == LibRCheevos.rc_error_t.RC_OK;
			}

			public void DoRequest()
			{
				var res = _lib.rc_api_init_award_achievement_request(out var api_req, ref _apiParams);
				Task = SendAPIRequestIfOK(res, ref api_req, CheevoUnlockTaskCallback);
			}

			public CheevoUnlockTask(string username, string api_token, int achievement_id, bool hardcore, string game_hash)
			{
				_apiParams = new(username, api_token, achievement_id, hardcore, game_hash);
				DoRequest();
			}
		}

		// keep a list of all cheevo unlock trigger tasks that have been queued
		// on Dispose(), we wait for all these to complete
		// on Update(), we clear out successfully completed tasks, any not completed will be resent
		private readonly List<CheevoUnlockTask> _queuedCheevoUnlockTasks = new();

		private bool CheevosActive { get; set; }
		private bool AllowUnofficialCheevos { get; set; }

		public class Cheevo
		{
			public int ID { get; }
			public int Points { get; }
			public LibRCheevos.rc_runtime_achievement_category_t Category { get; }
			public string Title { get; }
			public string Description { get; }
			public string Definition { get; }
			public string Author { get; }
			private string BadgeName { get; }
			public Bitmap BadgeUnlocked { get; private set; }
			public Bitmap BadgeLocked { get; private set; }
			public DateTime Created { get; }
			public DateTime Updated { get; }

			public bool IsSoftcoreUnlocked { get; set; }
			public bool IsHardcoreUnlocked { get; set; }

			public bool IsUnlocked(bool hardcore)
				=> hardcore ? IsHardcoreUnlocked : IsSoftcoreUnlocked;

			public void SetUnlocked(bool hardcore, bool unlocked)
			{
				if (hardcore)
				{
					IsHardcoreUnlocked = unlocked;
				}
				else
				{
					IsSoftcoreUnlocked = unlocked;
				}
			}

			public bool IsPrimed { get; set; }
			private Func<bool> AllowUnofficialCheevos { get; }
			public bool Invalid { get; set; }
			public bool IsEnabled => !Invalid && (IsOfficial || AllowUnofficialCheevos());
			public bool IsOfficial => Category is LibRCheevos.rc_runtime_achievement_category_t.RC_ACHIEVEMENT_CATEGORY_CORE;

			public void LoadImages()
			{
				GetImage(BadgeName, LibRCheevos.rc_api_image_type_t.RC_IMAGE_TYPE_ACHIEVEMENT, badge => BadgeUnlocked = badge);
				GetImage(BadgeName, LibRCheevos.rc_api_image_type_t.RC_IMAGE_TYPE_ACHIEVEMENT_LOCKED, badge => BadgeLocked = badge);
			}

			public Cheevo(in LibRCheevos.rc_api_achievement_definition_t cheevo, Func<bool> allowUnofficialCheevos)
			{
				ID = cheevo.id;
				Points = cheevo.points;
				Category = cheevo.category;
				Title = cheevo.Title;
				Description = cheevo.Description;
				Definition = cheevo.Definition;
				Author = cheevo.Author;
				BadgeName = cheevo.BadgeName;
				BadgeUnlocked = null;
				BadgeLocked = null;
				Created = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(cheevo.created).ToLocalTime();
				Updated = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(cheevo.updated).ToLocalTime();
				IsSoftcoreUnlocked = false;
				IsHardcoreUnlocked = false;
				IsPrimed = false;
				AllowUnofficialCheevos = allowUnofficialCheevos;
				Invalid = false;
			}

			public Cheevo(in Cheevo cheevo, Func<bool> allowUnofficialCheevos)
			{
				ID = cheevo.ID;
				Points = cheevo.Points;
				Category = cheevo.Category;
				Title = cheevo.Title;
				Description = cheevo.Description;
				Definition = cheevo.Definition;
				Author = cheevo.Author;
				BadgeName = cheevo.BadgeName;
				BadgeUnlocked = null;
				BadgeLocked = null;
				Created = cheevo.Created;
				Updated = cheevo.Updated;
				IsSoftcoreUnlocked = false;
				IsHardcoreUnlocked = false;
				IsPrimed = false;
				AllowUnofficialCheevos = allowUnofficialCheevos;
				Invalid = false;
			}
		}

		private readonly byte[] _cheevoFormatBuffer = new byte[1024];

		private string GetCheevoProgress(int id)
		{
			var len = _lib.rc_runtime_format_achievement_measured(ref _runtime, id, _cheevoFormatBuffer, _cheevoFormatBuffer.Length);
			return Encoding.ASCII.GetString(_cheevoFormatBuffer, 0, len);
		}

		private void ToggleUnofficialCheevos()
		{
			if (_gameData.GameID == 0)
			{
				AllowUnofficialCheevos ^= true;
				return;
			}

			_activeModeUnlocksTask.Wait();

			foreach (var cheevo in _gameData.CheevoEnumerable)
			{
				if (cheevo.IsEnabled && !cheevo.IsUnlocked(HardcoreMode))
				{
					_lib.rc_runtime_deactivate_achievement(ref _runtime, cheevo.ID);
				}
			}

			AllowUnofficialCheevos ^= true;

			foreach (var cheevo in _gameData.CheevoEnumerable)
			{
				if (cheevo.IsEnabled && !cheevo.IsUnlocked(HardcoreMode))
				{
					_lib.rc_runtime_activate_achievement(ref _runtime, cheevo.ID, cheevo.Definition, IntPtr.Zero, 0);
				}
			}
		}

		private void ToSoftcoreMode()
		{
			if (_gameData == null || _gameData.GameID == 0) return;

			_inactiveModeUnlocksTask.Wait();

			foreach (var cheevo in _gameData.CheevoEnumerable)
			{
				if (cheevo.IsEnabled && !cheevo.IsUnlocked(false))
				{
					_lib.rc_runtime_deactivate_achievement(ref _runtime, cheevo.ID);
				}
			}

			_activeModeUnlocksTask.Wait();

			foreach (var cheevo in _gameData.CheevoEnumerable)
			{
				if (cheevo.IsEnabled && !cheevo.IsUnlocked(true))
				{
					_lib.rc_runtime_activate_achievement(ref _runtime, cheevo.ID, cheevo.Definition, IntPtr.Zero, 0);
				}
			}

			Update();
		}
	}
}