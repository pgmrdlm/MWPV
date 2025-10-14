-- s_Combo_LogsDetailSelectByType.sql  (keep the filename)
SELECT
  cd.ComboDetailId AS ComboDet,
  cd.ComboTypeId   AS ComboTyp,
  cd.Seq,
  cd.Code,
  cd.Description,
  cd.Active,
  cd.CreatedUtc,
  cd.UpdatedUtc
FROM ComboDetail cd
JOIN ComboType ct ON ct.ComboTypeId = cd.ComboTypeId
WHERE ct.Active = 1 AND cd.Active = 1 AND ct.Code = @type_code
ORDER BY cd.Seq, cd.Code;
