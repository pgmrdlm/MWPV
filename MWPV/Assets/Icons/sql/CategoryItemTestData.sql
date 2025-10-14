-- === CategoryItem seeds for grid testing ====================================
-- Uses category name lookups so it works regardless of Category_Key values.

-- Helper: resolves a category key by name
-- (No DDL; just used inline in each INSERT via sub-select.)

-- 6 items → Application Forums
INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description, CI_Notes)
SELECT Category_Key, 'Forum – App One',        'Login for App One forum',        'seed'
FROM Category WHERE Category_Name='Application Forums';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description, CI_Notes)
SELECT Category_Key, 'Forum – App Two',        'Login for App Two forum',        'seed'
FROM Category WHERE Category_Name='Application Forums';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description, CI_Notes)
SELECT Category_Key, 'Forum – App Three',      'Login for App Three forum',      'seed'
FROM Category WHERE Category_Name='Application Forums';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description, CI_Notes)
SELECT Category_Key, 'Forum – App Four',       'Login for App Four forum',       'seed'
FROM Category WHERE Category_Name='Application Forums';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description, CI_Notes)
SELECT Category_Key, 'Forum – App Five',       'Login for App Five forum',       'seed'
FROM Category WHERE Category_Name='Application Forums';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description, CI_Notes)
SELECT Category_Key, 'Forum – App Six',        'Login for App Six forum',        'seed'
FROM Category WHERE Category_Name='Application Forums';

-- 1 item → Government
INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description, CI_Notes)
SELECT Category_Key, 'SSA Account',            'Social Security Administration', 'seed'
FROM Category WHERE Category_Name='Government';

-- 7 items → Google Accounts
INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description, CI_Notes)
SELECT Category_Key, 'Gmail – Personal',       'Primary Gmail account',          'seed'
FROM Category WHERE Category_Name='Google Accounts';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description, CI_Notes)
SELECT Category_Key, 'Gmail – Backup',         'Backup Gmail account',           'seed'
FROM Category WHERE Category_Name='Google Accounts';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description, CI_Notes)
SELECT Category_Key, 'Google Drive',           'Drive cloud storage',            'seed'
FROM Category WHERE Category_Name='Google Accounts';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description, CI_Notes)
SELECT Category_Key, 'YouTube',                'YouTube sign-in',                'seed'
FROM Category WHERE Category_Name='Google Accounts';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description, CI_Notes)
SELECT Category_Key, 'Google Photos',          'Photos library',                 'seed'
FROM Category WHERE Category_Name='Google Accounts';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description, CI_Notes)
SELECT Category_Key, 'Google Play',            'Play store',                     'seed'
FROM Category WHERE Category_Name='Google Accounts';

INSERT OR IGNORE INTO CategoryItem (Category_Key, CI_Name, CI_Description, CI_Notes)
SELECT Category_Key, 'Google Voice',           'Voice number',                   'seed'
FROM Category WHERE Category_Name='Google Accounts';
-- ============================================================================
