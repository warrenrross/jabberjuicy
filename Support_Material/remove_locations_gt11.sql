/* ============================================================
   remove_locations_gt11.sql
   JabberJuicy duplicate location cleanup

   Purpose:
   - move any orders tied to LocationID > 11 onto the matching
     canonical location with the lowest LocationID
   - delete all Location rows where LocationID > 11

   Important:
   - this script only targets LocationID > 11
   - it does NOT remove duplicates in the 5–11 range
   - if you actually want to clean up every duplicate described in
     AGENTS.md, this script should be broadened afterward
   ============================================================ */

SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Preview the rows that will be targeted.
    SELECT *
    FROM Location
    WHERE LocationID > 11
    ORDER BY LocationID;

    -- Build a duplicate-to-canonical mapping by matching on store attributes.
    IF OBJECT_ID('tempdb..#LocationRemap') IS NOT NULL
        DROP TABLE #LocationRemap;

    SELECT
        d.LocationID AS DuplicateLocationID,
        MIN(k.LocationID) AS KeepLocationID
    INTO #LocationRemap
    FROM Location d
    JOIN Location k
        ON ISNULL(d.LOC_StoreName, '') = ISNULL(k.LOC_StoreName, '')
       AND ISNULL(d.LOC_Address, '')   = ISNULL(k.LOC_Address, '')
       AND ISNULL(d.LOC_City, '')      = ISNULL(k.LOC_City, '')
       AND ISNULL(d.LOC_State, '')     = ISNULL(k.LOC_State, '')
       AND ISNULL(d.LOC_ZipCode, '')   = ISNULL(k.LOC_ZipCode, '')
       AND k.LocationID <= 11
    WHERE d.LocationID > 11
    GROUP BY d.LocationID;

    -- Stop if any target location does not have a matching canonical location.
    IF EXISTS (
        SELECT 1
        FROM Location d
        LEFT JOIN #LocationRemap r
            ON r.DuplicateLocationID = d.LocationID
        WHERE d.LocationID > 11
          AND r.KeepLocationID IS NULL
    )
    BEGIN
        RAISERROR('One or more LocationID > 11 rows could not be matched to a canonical location <= 11.', 16, 1);
    END;

    -- Preview which orders will be reassigned.
    SELECT
        o.OrderID,
        o.LocationID AS OldLocationID,
        r.KeepLocationID AS NewLocationID
    FROM [Order] o
    JOIN #LocationRemap r
        ON r.DuplicateLocationID = o.LocationID
    ORDER BY o.OrderID;

    -- Reassign orders off the higher duplicate locations.
    UPDATE o
    SET o.LocationID = r.KeepLocationID
    FROM [Order] o
    JOIN #LocationRemap r
        ON r.DuplicateLocationID = o.LocationID;

    -- Verify no orders still reference the higher IDs.
    IF EXISTS (
        SELECT 1
        FROM [Order]
        WHERE LocationID > 11
    )
    BEGIN
        RAISERROR('Orders still reference LocationID > 11 after remap. Delete cancelled.', 16, 1);
    END;

    -- Delete the higher-ID locations now that FK references are gone.
    DELETE FROM Location
    WHERE LocationID > 11;

    -- Final verification.
    SELECT *
    FROM Location
    ORDER BY LocationID;

    SELECT
        LocationID,
        COUNT(*) AS OrderCount
    FROM [Order]
    GROUP BY LocationID
    ORDER BY LocationID;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
