-- === CategoryItem seeds for grid testing ====================================
-- Uses category name lookups so it works regardless of Category_Key values.

-- Helper: resolves a category key by name
-- (No DDL; just used inline in each INSERT via sub-select.)

-- 6 items → Application Forums
INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description)
SELECT Category_Key, 'Forum – App One',        'Login for App One forum'
FROM Category WHERE Category_Name='Entertainment';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description)
SELECT Category_Key, 'Forum – App Two',        'Login for App Two forum'
FROM Category WHERE Category_Name='Entertainment';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description)
SELECT Category_Key, 'Forum – App Three',      'Login for App Three forum'
FROM Category WHERE Category_Name='Entertainment';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description)
SELECT Category_Key, 'Forum – App Four',       'Login for App Four forum'
FROM Category WHERE Category_Name='Entertainment';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description)
SELECT Category_Key, 'Forum – App Five',       'Login for App Five forum'
FROM Category WHERE Category_Name='Entertainment';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description)
SELECT Category_Key, 'Forum – App Six',        'Login for App Six forum'
FROM Category WHERE Category_Name='Entertainment';

-- 1 item → Government
INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description)
SELECT Category_Key, 'SSA Account',            'Social Security Administration'
FROM Category WHERE Category_Name='Entertainment';

-- 7 items → Google Accounts
INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description)
SELECT Category_Key, 'Gmail – Personal',       'Primary Gmail account'
FROM Category WHERE Category_Name='Entertainment';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description)
SELECT Category_Key, 'Gmail – Backup',         'Backup Gmail account'
FROM Category WHERE Category_Name='Entertainment';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description)
SELECT Category_Key, 'Google Drive',           'Drive cloud storage'
FROM Category WHERE Category_Name='Entertainment';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description)
SELECT Category_Key, 'YouTube',                'YouTube sign-in'
FROM Category WHERE Category_Name='Entertainment';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description)
SELECT Category_Key, 'Google Photos',          'Photos library'
FROM Category WHERE Category_Name='Entertainment';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description)
SELECT Category_Key, 'Google Play',            'Play store'
FROM Category WHERE Category_Name='Entertainment';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description)
SELECT Category_Key, 'Google Voice',           'Voice number'
FROM Category WHERE Category_Name='Entertainment';
-- ============================================================================
