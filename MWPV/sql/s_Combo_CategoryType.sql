SELECT
    d.Code        AS Code,
    d.Description AS Description
FROM ComboDetail d
JOIN ComboType t ON t.ComboTypeId = d.ComboTypeId
WHERE t.Code = 'category_types'
  AND COALESCE(d.Active, 1) = 1
ORDER BY COALESCE(d.Seq, 0), d.Description;
