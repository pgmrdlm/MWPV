/* File: sql/s_DbVersion_select_current.sql */

SELECT
    Id        AS Id,
    Version   AS Version,
    IsCurrent AS IsCurrent,
    AppliedOn AS CreatedAt
FROM DbVersion
WHERE IsCurrent = 1
ORDER BY Id DESC
LIMIT 1;
