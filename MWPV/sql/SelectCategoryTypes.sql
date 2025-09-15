SELECT
    d.Code        AS Code,
    d.Description AS Description
FROM ComboDetail d
WHERE d.ComboTypeId = 2
  AND COALESCE(d.Active, 1) = 1
ORDER BY COALESCE(d.Seq, 0), d.Description;
