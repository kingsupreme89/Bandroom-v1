-- Bandroom Supabase schema (System 1: Cloud Database Migration).
-- Run this in the Supabase project's SQL Editor once (Settings -> SQL Editor -> New query -> Run).
-- After running, put the project URL + anon key into Bandroom via ConfigStore.SaveSupabaseSettings
-- (Settings -> Cloud Sync in the app, once that panel exists) so CloudDatabaseService can reach it.

-- ---------------------------------------------------------------------------
-- team_profiles: what CloudDatabaseService actually reads/writes today (v1).
-- Mirrors ConfigStore's local ProfilesFolder\{team}.json 1:1 (config = the same TriggerEntry[]
-- JSON already saved to disk) so there's no lossy mapping between local and cloud. This is what
-- makes a team's profile visible/editable from the Supabase table editor on another device.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS team_profiles (
  team_name  TEXT PRIMARY KEY,
  config     JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Row-Level Security: for now this app has no per-user auth on the cloud mirror (it's a single
-- local install's data), so allow the anon key full read/write. Tighten this (scope to
-- auth.uid() = user_id) once System 1's multi-user auth story is built -- see the master prompt's
-- "Row-Level Security" implementation rule.
ALTER TABLE team_profiles ENABLE ROW LEVEL SECURITY;
CREATE POLICY "anon full access" ON team_profiles FOR ALL USING (true) WITH CHECK (true);

-- ---------------------------------------------------------------------------
-- Fully-normalized schema from BANDROOM_STREAMER_MASTER_PROMPT.md, System 1. Not consumed by
-- CloudDatabaseService yet -- kept here as the target schema for when System 3 (11-field song
-- metadata / marketplace queries like "all touchdown songs for SEC teams") actually needs
-- normalized team_id/trigger_id/song_id foreign keys instead of a JSONB blob per team.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS teams (
  id SERIAL PRIMARY KEY,
  name TEXT NOT NULL UNIQUE,
  abbreviation TEXT NOT NULL UNIQUE,
  conference TEXT,
  primary_color TEXT,
  secondary_color TEXT,
  logo_url TEXT,
  background_url TEXT,
  created_at TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE IF NOT EXISTS event_triggers (
  id SERIAL PRIMARY KEY,
  trigger_key TEXT NOT NULL UNIQUE,
  category TEXT NOT NULL,
  display_name TEXT NOT NULL,
  default_cooldown_seconds INTEGER NOT NULL DEFAULT 20,
  priority INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS songs (
  id SERIAL PRIMARY KEY,
  uploaded_by TEXT,
  team_id INTEGER REFERENCES teams(id),
  trigger_id INTEGER REFERENCES event_triggers(id),

  standard_title TEXT NOT NULL,
  standard_artist TEXT,
  school_abbrev TEXT NOT NULL,
  standardized_filename TEXT NOT NULL,
  primary_trigger TEXT NOT NULL,
  marketplace_category TEXT NOT NULL,
  recommended_trim_start_seconds REAL,
  recommended_trim_end_seconds REAL,
  recommended_reverb_preset TEXT DEFAULT 'Stadium',
  energy_level TEXT DEFAULT 'Mid',
  prominent_instrumentation TEXT,
  acoustic_description TEXT,

  integrated_lufs REAL,
  short_term_lufs REAL,
  true_peak_dbtp REAL,
  normalization_gain_db REAL,

  storage_path TEXT NOT NULL,
  file_size_bytes BIGINT,
  duration_seconds REAL,
  sample_rate INTEGER,
  bit_depth INTEGER,
  channels INTEGER,

  is_public BOOLEAN DEFAULT false,
  download_count INTEGER DEFAULT 0,

  created_at TIMESTAMPTZ DEFAULT now(),
  updated_at TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE IF NOT EXISTS team_configs (
  id SERIAL PRIMARY KEY,
  user_id TEXT,
  team_id INTEGER REFERENCES teams(id),
  trigger_id INTEGER REFERENCES event_triggers(id),
  song_id INTEGER REFERENCES songs(id),
  volume_override REAL DEFAULT 1.0,
  reverb_override TEXT,
  is_active BOOLEAN DEFAULT true,
  created_at TIMESTAMPTZ DEFAULT now(),
  updated_at TIMESTAMPTZ DEFAULT now(),
  UNIQUE(user_id, team_id, trigger_id)
);

CREATE TABLE IF NOT EXISTS activity_log (
  id SERIAL PRIMARY KEY,
  user_id TEXT,
  team_id INTEGER REFERENCES teams(id),
  event_key TEXT NOT NULL,
  song_id INTEGER REFERENCES songs(id),
  song_title TEXT,
  input_lufs REAL,
  applied_gain_db REAL,
  output_peak_dbfs REAL,
  play_duration_seconds REAL,
  game_clock TEXT,
  real_timestamp TIMESTAMPTZ DEFAULT now()
);
