-- ComboDetail_SelectByType.sql
-- Params: @type_code TEXT

SELECT
    cd.ComboDetailId AS ComboDet,
    cd.ComboTypeId,
    cd.Seq,
    cd.Code,
    cd.Description,
    cd.Active,
    cd.CreatedUtc,
    cd.UpdatedUtc
FROM ComboDetail cd
JOIN ComboType ct
  ON ct.ComboTypeId = cd.ComboTypeId
WHERE ct.Active = 1
  AND cd.Active = 1
  AND ct.Code   = @type_code
ORDER BY cd.Seq, cd.Code;
