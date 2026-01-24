CREATE USER 'machine1'@'%' IDENTIFIED BY '1199';
GRANT SELECT, UPDATE, insert ON casino_db.* TO 'machine1'@'%';



show grants for 'machine1'@'%';


INSERT INTO transaction (user_id, eur_amount, timestamp, details)
VALUES
    (1, 2.00, NOW(), 'Physical coin insert'),
    (1, 5.00, NOW(), 'Physical coin insert'),
    (1, 10.00, NOW(), 'Physical coin insert');

INSERT INTO physical_deposit (transaction_id, session_code, coin_value, confirmed)
VALUES (1, 1111, 2.00, 1);

-- Transaction ID 2
INSERT INTO physical_deposit (transaction_id, session_code, coin_value, confirmed)
VALUES (2, 2222, 5.00, 1);

-- Transaction ID 3
INSERT INTO physical_deposit (transaction_id, session_code, coin_value, confirmed)
VALUES (3, 3333, 10.00, 1);

INSERT INTO game_session
(user_id, game_type, bet_amount, result, win_amount, balance_before, balance_after, timestamp)
VALUES
-- heute
(1, 'Roulette', 10, 'Loss', 0, 100, 90, UTC_TIMESTAMP() - INTERVAL 1 HOUR),
(1, 'Blackjack', 20, 'Win', 40, 90, 130, UTC_TIMESTAMP() - INTERVAL 2 HOUR),

-- letzte Woche
(2, 'Roulette', 50, 'Loss', 0, 200, 150, UTC_TIMESTAMP() - INTERVAL 3 DAY),
(2, 'Blackjack', 100, 'Win', 200, 150, 350, UTC_TIMESTAMP() - INTERVAL 5 DAY),

-- älter
(3, 'Roulette', 5, 'Draw', 0, 50, 50, UTC_TIMESTAMP() - INTERVAL 40 DAY);

/* =========================
   CRYPTO DEPOSITS
   ========================= */

/* =========================
   HARDWARE DEPOSITS
   ========================= */

/* =========================
   LOGS
   ========================= */
INSERT INTO log
(action_type, user_type, user_id, description, date)
VALUES
    ('Info', 'System', NULL, 'System gestartet', UTC_TIMESTAMP()),
    ('UserAction', 'User', 1, 'User played Roulette', UTC_TIMESTAMP() - INTERVAL 30 MINUTE),
    ('AdminAction', 'Admin', 1, 'Admin approved withdrawal', UTC_TIMESTAMP() - INTERVAL 2 HOUR),
    ('Warning', 'System', NULL, 'Low wallet balance', UTC_TIMESTAMP() - INTERVAL 1 DAY),
    ('Error', 'System', NULL, 'Blockchain timeout', UTC_TIMESTAMP() - INTERVAL 3 DAY);