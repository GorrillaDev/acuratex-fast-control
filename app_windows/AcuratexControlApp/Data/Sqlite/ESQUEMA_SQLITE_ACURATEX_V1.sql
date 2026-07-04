-- Acuratex Profile Database v1
-- Fuente de verdad local para perfiles completos de cabezal.
-- SQLite en la app Windows. El ESP32 NO consulta esta base durante cada movimiento.

PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;

BEGIN;

CREATE TABLE IF NOT EXISTS schema_meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

INSERT OR REPLACE INTO schema_meta(key, value) VALUES
('schema_name', 'acuratex_profiles'),
('schema_version', '1');

-- Identidad estable de un perfil. Sus versiones son inmutables.
CREATE TABLE IF NOT EXISTS profiles (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    profile_key   TEXT NOT NULL UNIQUE,
    program_number INTEGER CHECK (program_number IS NULL OR program_number >= 1),
    display_name  TEXT NOT NULL,
    description   TEXT NOT NULL DEFAULT '',
    enabled       INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    created_utc   TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_utc   TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
);

CREATE TABLE IF NOT EXISTS profile_versions (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    profile_id      INTEGER NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
    version_number  INTEGER NOT NULL CHECK (version_number >= 1),
    schema_version  INTEGER NOT NULL DEFAULT 1 CHECK (schema_version >= 1),
    notes           TEXT NOT NULL DEFAULT '',
    source_kind     TEXT NOT NULL DEFAULT 'SQLITE'
                    CHECK (source_kind IN ('SQLITE', 'CPP_IMPORT', 'MANUAL')),
    crc32           INTEGER,
    is_published    INTEGER NOT NULL DEFAULT 0 CHECK (is_published IN (0, 1)),
    created_utc     TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    UNIQUE(profile_id, version_number)
);

-- INIT: misma semÃ¡ntica que HeadInitCommandSequence.
CREATE TABLE IF NOT EXISTS init_config (
    profile_version_id       INTEGER PRIMARY KEY
                             REFERENCES profile_versions(id) ON DELETE CASCADE,
    phase1_step_delay_ms      INTEGER NOT NULL DEFAULT 0 CHECK (phase1_step_delay_ms >= 0),
    phase_gap_ms              INTEGER NOT NULL DEFAULT 0 CHECK (phase_gap_ms >= 0),
    phase2_step_delay_ms      INTEGER NOT NULL DEFAULT 0 CHECK (phase2_step_delay_ms >= 0)
);

CREATE TABLE IF NOT EXISTS init_steps (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    profile_version_id  INTEGER NOT NULL
                        REFERENCES profile_versions(id) ON DELETE CASCADE,
    phase               INTEGER NOT NULL CHECK (phase IN (1, 2)),
    step_order          INTEGER NOT NULL CHECK (step_order >= 0),
    step_type           TEXT NOT NULL CHECK (step_type IN ('CAN', 'WAIT', 'STATUS')),
    bus                 INTEGER CHECK (bus IN (1, 2)),
    can_id              INTEGER CHECK (can_id BETWEEN 0 AND 536870911),
    dlc                 INTEGER CHECK (dlc BETWEEN 0 AND 8),
    data                BLOB,
    wait_ms             INTEGER CHECK (wait_ms >= 0),
    raw_text            TEXT NOT NULL DEFAULT '',
    CHECK (
        (step_type = 'CAN'
            AND bus IS NOT NULL
            AND can_id IS NOT NULL
            AND dlc IS NOT NULL
            AND data IS NOT NULL
            AND length(data) = dlc)
        OR
        (step_type = 'WAIT'
            AND wait_ms IS NOT NULL)
        OR
        (step_type = 'STATUS')
    ),
    UNIQUE(profile_version_id, phase, step_order)
);

-- TESTEO: misma semÃ¡ntica que HeadTesteoCommandProfile.
CREATE TABLE IF NOT EXISTS testeo_profiles (
    profile_version_id          INTEGER PRIMARY KEY
                                REFERENCES profile_versions(id) ON DELETE CASCADE,
    ping_can_id                 INTEGER NOT NULL CHECK (ping_can_id BETWEEN 0 AND 536870911),
    ping_dlc                    INTEGER NOT NULL CHECK (ping_dlc BETWEEN 0 AND 8),
    ping_data                   BLOB NOT NULL,
    response_can_id             INTEGER NOT NULL CHECK (response_can_id BETWEEN 0 AND 536870911),
    reset_can_id                INTEGER NOT NULL CHECK (reset_can_id BETWEEN 0 AND 536870911),
    reset_dlc                   INTEGER NOT NULL CHECK (reset_dlc BETWEEN 0 AND 8),
    reset_data                  BLOB NOT NULL,
    success_code                INTEGER NOT NULL CHECK (success_code BETWEEN 0 AND 255),
    missing_expansion_code      INTEGER NOT NULL CHECK (missing_expansion_code BETWEEN 0 AND 255),
    missing_force_code          INTEGER NOT NULL CHECK (missing_force_code BETWEEN 0 AND 255),
    force_board_1_code          INTEGER NOT NULL CHECK (force_board_1_code BETWEEN 0 AND 255),
    force_board_2_code          INTEGER NOT NULL CHECK (force_board_2_code BETWEEN 0 AND 255),
    max_tries                   INTEGER NOT NULL CHECK (max_tries > 0),
    response_timeout_ms         INTEGER NOT NULL CHECK (response_timeout_ms >= 0),
    retry_delay_ms              INTEGER NOT NULL CHECK (retry_delay_ms >= 0),
    reset_debounce_ms           INTEGER NOT NULL CHECK (reset_debounce_ms >= 0),
    CHECK (length(ping_data) = ping_dlc),
    CHECK (length(reset_data) = reset_dlc)
);

-- DEN, SIC y FEET: misma semÃ¡ntica que HeadMotionCommandProfile.
CREATE TABLE IF NOT EXISTS motion_modules (
    id                       INTEGER PRIMARY KEY AUTOINCREMENT,
    profile_version_id       INTEGER NOT NULL
                             REFERENCES profile_versions(id) ON DELETE CASCADE,
    module_type              TEXT NOT NULL CHECK (module_type IN ('DEN', 'SIC', 'FEET')),
    can_id                   INTEGER NOT NULL CHECK (can_id BETWEEN 0 AND 536870911),
    opcode                   INTEGER NOT NULL CHECK (opcode BETWEEN 0 AND 255),
    motor_index_base         INTEGER NOT NULL CHECK (motor_index_base BETWEEN 0 AND 255),
    instance_count           INTEGER NOT NULL CHECK (instance_count >= 0),
    run_period_ms            INTEGER NOT NULL DEFAULT 0 CHECK (run_period_ms >= 0),
    alternate_run_period_ms  INTEGER NOT NULL DEFAULT 0 CHECK (alternate_run_period_ms >= 0),
    UNIQUE(profile_version_id, module_type)
);

CREATE TABLE IF NOT EXISTS motion_positions (
    motion_module_id  INTEGER NOT NULL
                      REFERENCES motion_modules(id) ON DELETE CASCADE,
    position_order    INTEGER NOT NULL CHECK (position_order >= 0),
    position_value    INTEGER NOT NULL CHECK (position_value BETWEEN 0 AND 65535),
    PRIMARY KEY(motion_module_id, position_order)
);

CREATE TABLE IF NOT EXISTS motion_sequences (
    motion_module_id  INTEGER NOT NULL
                      REFERENCES motion_modules(id) ON DELETE CASCADE,
    sequence_kind     TEXT NOT NULL CHECK (sequence_kind IN ('RUN', 'ALTERNATE')),
    step_order        INTEGER NOT NULL CHECK (step_order >= 0),
    position_index    INTEGER NOT NULL CHECK (position_index >= 0),
    PRIMARY KEY(motion_module_id, sequence_kind, step_order)
);

-- J: misma semÃ¡ntica que HeadJCommandProfile.
CREATE TABLE IF NOT EXISTS j_modules (
    profile_version_id    INTEGER PRIMARY KEY
                          REFERENCES profile_versions(id) ON DELETE CASCADE,
    can_id                INTEGER NOT NULL CHECK (can_id BETWEEN 0 AND 536870911),
    opcode                INTEGER NOT NULL CHECK (opcode BETWEEN 0 AND 255),
    instance_index_base   INTEGER NOT NULL CHECK (instance_index_base BETWEEN 0 AND 255),
    instance_count        INTEGER NOT NULL CHECK (instance_count >= 0),
    channel_count         INTEGER NOT NULL CHECK (channel_count BETWEEN 0 AND 8),
    initial_register      INTEGER NOT NULL CHECK (initial_register BETWEEN 0 AND 255),
    on_all_register       INTEGER NOT NULL CHECK (on_all_register BETWEEN 0 AND 255),
    off_all_register      INTEGER NOT NULL CHECK (off_all_register BETWEEN 0 AND 255),
    run_period_ms         INTEGER NOT NULL DEFAULT 0 CHECK (run_period_ms >= 0)
);

-- YARN y STITCH: misma semÃ¡ntica que HeadCascadeCommandProfile.
CREATE TABLE IF NOT EXISTS cascade_modules (
    id                       INTEGER PRIMARY KEY AUTOINCREMENT,
    profile_version_id       INTEGER NOT NULL
                             REFERENCES profile_versions(id) ON DELETE CASCADE,
    module_type              TEXT NOT NULL CHECK (module_type IN ('YARN', 'STITCH')),
    can_id                   INTEGER NOT NULL CHECK (can_id BETWEEN 0 AND 536870911),
    opcode                   INTEGER NOT NULL CHECK (opcode BETWEEN 0 AND 255),
    addresses_per_instance   INTEGER NOT NULL CHECK (addresses_per_instance > 0),
    instance_count           INTEGER NOT NULL CHECK (instance_count >= 0),
    on_value                 INTEGER NOT NULL CHECK (on_value BETWEEN 0 AND 255),
    off_value                INTEGER NOT NULL CHECK (off_value BETWEEN 0 AND 255),
    run_period_ms            INTEGER NOT NULL DEFAULT 0 CHECK (run_period_ms >= 0),
    UNIQUE(profile_version_id, module_type)
);

CREATE TABLE IF NOT EXISTS cascade_addresses (
    cascade_module_id  INTEGER NOT NULL
                       REFERENCES cascade_modules(id) ON DELETE CASCADE,
    address_order      INTEGER NOT NULL CHECK (address_order >= 0),
    address_value      INTEGER NOT NULL CHECK (address_value BETWEEN 0 AND 255),
    PRIMARY KEY(cascade_module_id, address_order)
);

-- STOP: misma semÃ¡ntica que HeadStopCommandProfile.
CREATE TABLE IF NOT EXISTS stop_profiles (
    profile_version_id  INTEGER PRIMARY KEY
                        REFERENCES profile_versions(id) ON DELETE CASCADE,
    sends_can_frame     INTEGER NOT NULL DEFAULT 0 CHECK (sends_can_frame IN (0, 1)),
    bus                 INTEGER CHECK (bus IN (1, 2)),
    can_id              INTEGER CHECK (can_id BETWEEN 0 AND 536870911),
    dlc                 INTEGER CHECK (dlc BETWEEN 0 AND 8),
    data                BLOB,
    CHECK (
        (sends_can_frame = 0)
        OR
        (sends_can_frame = 1
            AND bus IS NOT NULL
            AND can_id IS NOT NULL
            AND dlc IS NOT NULL
            AND data IS NOT NULL
            AND length(data) = dlc)
    )
);

-- Acciones dinÃ¡micas adicionales; se cargan una vez a RAM.
CREATE TABLE IF NOT EXISTS actions (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    profile_version_id  INTEGER NOT NULL
                        REFERENCES profile_versions(id) ON DELETE CASCADE,
    action_name         TEXT NOT NULL,
    category            TEXT NOT NULL DEFAULT 'CUSTOM',
    enabled             INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    UNIQUE(profile_version_id, action_name)
);

CREATE TABLE IF NOT EXISTS action_steps (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    action_id    INTEGER NOT NULL REFERENCES actions(id) ON DELETE CASCADE,
    step_order   INTEGER NOT NULL CHECK (step_order >= 0),
    step_type    TEXT NOT NULL CHECK (step_type IN ('CAN', 'WAIT', 'STATUS')),
    bus          INTEGER CHECK (bus IN (1, 2)),
    can_id       INTEGER CHECK (can_id BETWEEN 0 AND 536870911),
    dlc          INTEGER CHECK (dlc BETWEEN 0 AND 8),
    data         BLOB,
    wait_ms      INTEGER CHECK (wait_ms >= 0),
    CHECK (
        (step_type = 'CAN'
            AND bus IS NOT NULL
            AND can_id IS NOT NULL
            AND dlc IS NOT NULL
            AND data IS NOT NULL
            AND length(data) = dlc)
        OR
        (step_type = 'WAIT'
            AND wait_ms IS NOT NULL)
        OR
        (step_type = 'STATUS')
    ),
    UNIQUE(action_id, step_order)
);

-- Registro de paquetes compilados para el ESP32.
CREATE TABLE IF NOT EXISTS exported_packages (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    profile_version_id  INTEGER NOT NULL
                        REFERENCES profile_versions(id) ON DELETE CASCADE,
    package_format      TEXT NOT NULL DEFAULT 'ACX1',
    package_path        TEXT NOT NULL,
    package_crc32       INTEGER NOT NULL,
    package_size        INTEGER NOT NULL CHECK (package_size > 0),
    created_utc         TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
);

CREATE INDEX IF NOT EXISTS idx_profile_versions_profile
    ON profile_versions(profile_id, version_number DESC);

CREATE INDEX IF NOT EXISTS idx_actions_profile_version
    ON actions(profile_version_id, action_name);

CREATE VIEW IF NOT EXISTS v_profile_summary AS
SELECT
    p.id AS profile_id,
    p.profile_key,
    p.program_number,
    p.display_name,
    pv.id AS profile_version_id,
    pv.version_number,
    pv.schema_version,
    pv.crc32,
    pv.is_published,
    (SELECT COUNT(*) FROM actions a WHERE a.profile_version_id = pv.id) AS action_count,
    (SELECT COUNT(*) FROM motion_modules m WHERE m.profile_version_id = pv.id) AS motion_module_count,
    (SELECT COUNT(*) FROM cascade_modules c WHERE c.profile_version_id = pv.id) AS cascade_module_count
FROM profiles p
JOIN profile_versions pv ON pv.profile_id = p.id;

COMMIT;


